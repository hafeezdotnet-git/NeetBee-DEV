using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthenticationService.Configuration;
using AuthenticationService.Data;
using AuthenticationService.Entities;
using AuthenticationService.Models;
using NeetBee.Common.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthenticationService.Services;

public sealed class AuthService(
    AppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IOptions<AuthOptions> options,
    IHttpClientFactory httpClientFactory,
    INotificationClient notifications,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidRefreshMessage = "The refresh token is invalid.";

    /// <summary>Name of the HTTP client (registered in Program.cs) that calls Google's userinfo endpoint.</summary>
    public const string GoogleUserInfoClient = "google-userinfo";

    /// <summary>How long an emailed code stays valid.</summary>
    private const int OtpLifetimeMinutes = 10;

    /// <summary>Minimum wait before another code can be requested for the same address.</summary>
    private const int OtpCooldownSeconds = 60;

    /// <summary>Wrong guesses allowed per code before it stops working.</summary>
    private const int OtpMaxAttempts = 5;

    private readonly JwtOptions _jwt = options.Value.Jwt;

    /// <summary>
    /// Creates a username and password account. Rejects invalid emails, weak passwords, and duplicate
    /// usernames or emails. Passwords are never logged.
    /// </summary>
    public async Task<AuthOutcome> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var email = request.Email.Trim().ToLowerInvariant();
        logger.LogInformation("Register started for user {UserName} with email {Email}", userName, MaskEmail(email));

        if (!IsValidEmail(email))
        {
            logger.LogWarning("Register rejected for user {UserName}: email address is not valid", userName);
            return new AuthOutcome(AuthStatus.ValidationFailed, Message: "Email address is not valid.");
        }

        var passwordError = PasswordPolicy.Validate(request.Password);
        if (passwordError is not null)
        {
            logger.LogWarning("Register rejected for user {UserName}: password policy failed", userName);
            return new AuthOutcome(AuthStatus.ValidationFailed, Message: passwordError);
        }

        var exists = await db.Users.AnyAsync(u => u.Email == email || u.UserName == userName, cancellationToken);
        if (exists)
        {
            logger.LogWarning("Register rejected for user {UserName}: username or email already exists", userName);
            return new AuthOutcome(AuthStatus.Conflict, Message: "An account with this email or username already exists.");
        }

        var user = new User
        {
            UserName = userName,
            Email = email,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Lost a race with another registration using the same email or username.
            logger.LogWarning(ex, "Register for user {UserName} lost a race with a duplicate account", userName);
            return new AuthOutcome(AuthStatus.Conflict, Message: "An account with this email or username already exists.");
        }

        logger.LogInformation("User registered: UserId {UserId}, UserName {UserName}", user.Id, userName);
        return new AuthOutcome(AuthStatus.Ok, UserId: user.Id);
    }

    /// <summary>
    /// Checks a username or email and password. On success, issues a refresh token and an access token.
    /// Failed attempts return the same generic message, so the response does not reveal which accounts exist.
    /// </summary>
    public async Task<AuthOutcome> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var identifier = request.Username.Trim().ToLowerInvariant();
        logger.LogInformation("Login attempt for {Identifier} from {IpAddress}", MaskEmail(identifier), ipAddress);

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email == identifier || u.UserName == identifier, cancellationToken);

        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Login failed for {Identifier}: account not found or inactive", MaskEmail(identifier));
            return InvalidCredentials();
        }

        // Gmail-only accounts have no password, so the username and password form cannot sign them in.
        if (user.PasswordHash is null)
        {
            logger.LogWarning("Login failed for user {UserId}: account has no password (Gmail or email-code account)", user.Id);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Login failed for user {UserId}: wrong password", user.Id);
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            logger.LogInformation("Password hash upgraded for user {UserId}", user.Id);
        }

        var now = DateTime.UtcNow;
        var rawRefresh = tokens.CreateRefreshToken();
        AddRefreshToken(user.Id, Guid.NewGuid(), TokenHasher.Hash(rawRefresh), ipAddress, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Login succeeded for user {UserId}", user.Id);
        return new AuthOutcome(AuthStatus.Ok, Tokens: BuildTokenResponse(user, rawRefresh));
    }

    /// <summary>
    /// Rotates a refresh token. The old token is revoked and a new one is issued in the same family.
    /// If an already-used token is presented again, the whole family is revoked.
    /// </summary>
    public async Task<AuthOutcome> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        logger.LogDebug("Refresh requested from {IpAddress}", ipAddress);
        var now = DateTime.UtcNow;
        var presentedHash = TokenHasher.Hash(refreshToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var stored = await db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == presentedHash, cancellationToken);

        if (stored is null)
        {
            logger.LogWarning("Refresh failed: refresh token not found");
            return new AuthOutcome(AuthStatus.Invalid, Message: InvalidRefreshMessage);
        }

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByTokenHash is not null)
            {
                // A token that was already rotated has been presented again: treat the whole chain as compromised.
                await RevokeFamilyAsync(stored.FamilyId, ipAddress, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogWarning("Refresh token reuse detected. Token family {FamilyId} revoked.", stored.FamilyId);
                return new AuthOutcome(AuthStatus.ReuseDetected, Message: InvalidRefreshMessage);
            }

            logger.LogWarning("Refresh failed: token already revoked in family {FamilyId}", stored.FamilyId);
            return new AuthOutcome(AuthStatus.Revoked, Message: InvalidRefreshMessage);
        }

        if (stored.ExpiresAt <= now)
        {
            logger.LogInformation("Refresh failed: token expired in family {FamilyId}", stored.FamilyId);
            return new AuthOutcome(AuthStatus.Expired, Message: "The refresh token has expired.");
        }

        if (!stored.User.IsActive)
        {
            logger.LogWarning("Refresh failed: user {UserId} is inactive", stored.UserId);
            return new AuthOutcome(AuthStatus.Invalid, Message: InvalidRefreshMessage);
        }

        var newRaw = tokens.CreateRefreshToken();
        var newHash = TokenHasher.Hash(newRaw);

        // Atomic compare-and-set: only one concurrent request can revoke this row.
        var rotated = await db.RefreshTokens
            .Where(r => r.Id == stored.Id && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RevokedAt, now)
                .SetProperty(r => r.RevokedByIp, ipAddress)
                .SetProperty(r => r.ReplacedByTokenHash, newHash), cancellationToken);

        if (rotated == 0)
        {
            // A concurrent request already consumed this token. Revoke the chain rather than issue a second valid token.
            await RevokeFamilyAsync(stored.FamilyId, ipAddress, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Concurrent refresh of one token detected. Token family {FamilyId} revoked.", stored.FamilyId);
            return new AuthOutcome(AuthStatus.ReuseDetected, Message: InvalidRefreshMessage);
        }

        AddRefreshToken(stored.UserId, stored.FamilyId, newHash, ipAddress, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Refresh succeeded for user {UserId}, family {FamilyId}", stored.UserId, stored.FamilyId);
        return new AuthOutcome(AuthStatus.Ok, Tokens: BuildTokenResponse(stored.User, newRaw));
    }

    /// <summary>
    /// Revokes every refresh token in the family that the given token belongs to. Unknown tokens are ignored,
    /// so logout does not reveal which tokens exist.
    /// </summary>
    public async Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Logout requested from {IpAddress}", ipAddress);
        var hash = TokenHasher.Hash(refreshToken);

        var familyId = await db.RefreshTokens
            .AsNoTracking()
            .Where(r => r.TokenHash == hash)
            .Select(r => (Guid?)r.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);

        // Unknown tokens are ignored so logout does not reveal which tokens exist.
        if (familyId is null)
        {
            logger.LogDebug("Logout ignored: refresh token not found");
            return;
        }

        await RevokeFamilyAsync(familyId.Value, ipAddress, DateTime.UtcNow, cancellationToken);
        logger.LogInformation("Logout revoked token family {FamilyId}", familyId.Value);
    }

    /// <summary>
    /// Signs in with a Gmail (Google) account, or creates one on first use. This serves both "Login with Gmail"
    /// and "Register with Gmail" on the web app.
    /// The caller passes the Google access token. This method asks Google who the token belongs to, so the
    /// identity is never trusted from the web app alone. Only a verified Gmail address is accepted.
    /// Accounts are matched by email. If an account already exists for that address, the user is signed in to it.
    /// A new account is created without a password, so it can only sign in with Gmail.
    /// </summary>
    public async Task<AuthOutcome> GoogleSignInAsync(string accessToken, string? ipAddress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Google sign-in attempt from {IpAddress}", ipAddress);
        var profile = await GetGoogleProfileAsync(accessToken, cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Email))
        {
            logger.LogWarning("Google sign-in failed: token was rejected or profile had no email");
            return new AuthOutcome(AuthStatus.Invalid, Message: "Google sign-in could not be verified. Please try again.");
        }

        if (!profile.EmailVerified)
        {
            logger.LogWarning("Google sign-in rejected: email {Email} is not verified by Google", MaskEmail(profile.Email));
            return new AuthOutcome(AuthStatus.Invalid, Message: "Your Google email address is not verified.");
        }

        var email = profile.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is not null && !user.IsActive)
        {
            logger.LogWarning("Google sign-in rejected: account {UserId} is disabled", user.Id);
            return new AuthOutcome(AuthStatus.Invalid, Message: "This account is disabled.");
        }

        if (user is null)
        {
            user = new User
            {
                UserName = await CreateUniqueUserNameAsync(email, cancellationToken),
                Email = email,
                PasswordHash = null, // Gmail-only account: no password
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another request created this account at the same moment. The user can simply try again.
                logger.LogWarning(ex, "Google sign-in for {Email} lost a race with a duplicate account", MaskEmail(email));
                return new AuthOutcome(AuthStatus.Conflict, Message: "Google sign-in could not complete. Please try again.");
            }

            logger.LogInformation("Created Gmail account UserId {UserId}, UserName {UserName}", user.Id, user.UserName);
        }

        var now = DateTime.UtcNow;
        var rawRefresh = tokens.CreateRefreshToken();
        AddRefreshToken(user.Id, Guid.NewGuid(), TokenHasher.Hash(rawRefresh), ipAddress, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Google sign-in succeeded for user {UserId}", user.Id);
        return new AuthOutcome(AuthStatus.Ok, Tokens: BuildTokenResponse(user, rawRefresh));
    }

    /// <summary>
    /// Emails a 6-digit sign-in code. Used by "Sign in with an email code" and "Sign up with an email code".
    /// The code is valid for 10 minutes and can be tried 5 times. A new code can be requested once every 60 seconds.
    /// Only a keyed hash of the code is stored, never the code itself.
    /// </summary>
    public async Task<AuthOutcome> RequestEmailCodeAsync(string emailAddress, CancellationToken cancellationToken)
    {
        var email = emailAddress.Trim().ToLowerInvariant();
        logger.LogInformation("Email code requested for {Email}", MaskEmail(email));

        if (!IsValidEmail(email))
        {
            logger.LogWarning("Email code rejected: address is not valid ({Email})", MaskEmail(email));
            return new AuthOutcome(AuthStatus.ValidationFailed, Message: "Email address is not valid.");
        }

        var now = DateTime.UtcNow;
        var lastRequested = await db.EmailOtpCodes
            .Where(c => c.Email == email)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastRequested is not null && lastRequested > now.AddSeconds(-OtpCooldownSeconds))
        {
            logger.LogWarning("Email code rate-limited for {Email}: a code was requested less than {Seconds} seconds ago", MaskEmail(email), OtpCooldownSeconds);
            return new AuthOutcome(AuthStatus.RateLimited, Message: "Please wait a minute before requesting another code.");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.EmailOtpCodes.Add(new EmailOtpCode
        {
            Email = email,
            CodeHash = HashOtp(email, code),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpLifetimeMinutes),
            Attempts = 0
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Email code stored for {Email}, expires in {Minutes} minutes", MaskEmail(email), OtpLifetimeMinutes);

        // NotificationService sends it by email; the wording is its "Otp" template.
        var sent = await notifications.SendAsync(
            NotificationChannel.Email, email, NotificationType.Otp,
            new NotificationData { Code = code, ExpiresInMinutes = OtpLifetimeMinutes }, cancellationToken);

        if (!sent)
        {
            logger.LogWarning("Email code could not be sent to {Email}", MaskEmail(email));
            return new AuthOutcome(AuthStatus.Invalid, Message: "The sign-in code could not be sent. Please try again later.");
        }

        logger.LogInformation("Email code sent to {Email}", MaskEmail(email));
        return new AuthOutcome(AuthStatus.Ok);
    }

    /// <summary>
    /// Checks an emailed code and signs the user in, creating the account on first use. A code works once.
    /// Wrong guesses count toward the 5-attempt limit, and the comparison is constant-time.
    /// The code itself is never logged.
    /// </summary>
    public async Task<AuthOutcome> VerifyEmailCodeAsync(string emailAddress, string code, string? ipAddress, CancellationToken cancellationToken)
    {
        var email = emailAddress.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        logger.LogInformation("Email code verification for {Email} from {IpAddress}", MaskEmail(email), ipAddress);

        var entry = await db.EmailOtpCodes
            .Where(c => c.Email == email && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            logger.LogWarning("Email code verification failed for {Email}: no active code", MaskEmail(email));
            return InvalidCode();
        }

        if (entry.Attempts >= OtpMaxAttempts)
        {
            logger.LogWarning("Email code verification failed for {Email}: attempt limit reached", MaskEmail(email));
            return InvalidCode();
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(entry.CodeHash),
            Encoding.UTF8.GetBytes(HashOtp(email, code)));

        if (!matches)
        {
            // Count the wrong guess in the database, so the limit holds across requests.
            await db.EmailOtpCodes
                .Where(c => c.Id == entry.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken);
            logger.LogWarning("Email code wrong for {Email}: attempt {Attempt} of {Max}", MaskEmail(email), entry.Attempts + 1, OtpMaxAttempts);
            return InvalidCode();
        }

        // Use the code atomically. If another request used it first, this request fails.
        var consumed = await db.EmailOtpCodes
            .Where(c => c.Id == entry.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        if (consumed == 0)
        {
            logger.LogWarning("Email code for {Email} was already used by a concurrent request", MaskEmail(email));
            return InvalidCode();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is not null && !user.IsActive)
        {
            logger.LogWarning("Email code sign-in rejected: account {UserId} is disabled", user.Id);
            return new AuthOutcome(AuthStatus.Invalid, Message: "This account is disabled.");
        }

        if (user is null)
        {
            user = new User
            {
                UserName = await CreateUniqueUserNameAsync(email, cancellationToken),
                Email = email,
                PasswordHash = null, // email-code account: no password
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another request created this account at the same moment. The user can try again.
                logger.LogWarning(ex, "Email code sign-in for {Email} lost a race with a duplicate account", MaskEmail(email));
                return new AuthOutcome(AuthStatus.Conflict, Message: "Sign-in could not complete. Please try again.");
            }

            logger.LogInformation("Created email-code account UserId {UserId}, UserName {UserName}", user.Id, user.UserName);
        }

        var rawRefresh = tokens.CreateRefreshToken();
        AddRefreshToken(user.Id, Guid.NewGuid(), TokenHasher.Hash(rawRefresh), ipAddress, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Email code sign-in succeeded for user {UserId}", user.Id);
        return new AuthOutcome(AuthStatus.Ok, Tokens: BuildTokenResponse(user, rawRefresh));
    }

    /// <summary>Keyed hash of a sign-in code. The signing key is the secret, so a leaked table alone cannot be brute-forced.</summary>
    private string HashOtp(string email, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_jwt.SigningKey),
            Encoding.UTF8.GetBytes($"{email}:{code}")));

    private static AuthOutcome InvalidCode() =>
        new(AuthStatus.Invalid, Message: "The code is incorrect or has expired. Request a new code.");

    /// <summary>Asks Google's userinfo endpoint who the access token belongs to. Returns null if Google rejects it.</summary>
    private async Task<GoogleProfile?> GetGoogleProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Calling Google userinfo endpoint");
            var client = httpClientFactory.CreateClient(GoogleUserInfoClient);
            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google userinfo returned status {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var profile = await response.Content.ReadFromJsonAsync<GoogleProfile>(cancellationToken);
            logger.LogDebug("Google userinfo returned a profile: {HasEmail}", profile?.Email is not null);
            return profile;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not reach Google to verify a Gmail sign-in token.");
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Google returned an unexpected userinfo response.");
            return null;
        }
    }

    /// <summary>
    /// Builds a username from the part of the Gmail address before the @, keeping only letters, digits, '.', '_' and '-'.
    /// Adds random digits if the name is taken.
    /// </summary>
    private async Task<string> CreateUniqueUserNameAsync(string email, CancellationToken cancellationToken)
    {
        var localPart = email[..email.IndexOf('@')];
        var cleaned = new string(localPart.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (cleaned.Length < 3)
            cleaned = "user" + cleaned;
        cleaned = cleaned[..Math.Min(cleaned.Length, 40)].ToLowerInvariant();

        var candidate = cleaned;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var taken = await db.Users.AnyAsync(u => u.UserName == candidate, cancellationToken);
            if (!taken)
            {
                logger.LogDebug("Chose unique username {UserName} on attempt {Attempt}", candidate, attempt + 1);
                return candidate;
            }

            logger.LogDebug("Username {UserName} is taken, trying another", candidate);
            candidate = cleaned + Random.Shared.Next(1000, 99999);
        }

        var fallback = "user" + Guid.NewGuid().ToString("N");
        logger.LogWarning("No free username after 10 attempts; using fallback {UserName}", fallback);
        return fallback;
    }

    /// <summary>The fields of Google's userinfo response that this service uses.</summary>
    private sealed record GoogleProfile(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("email_verified")] bool EmailVerified);

    private void AddRefreshToken(int userId, Guid familyId, string tokenHash, string? ipAddress, DateTime now)
    {
        logger.LogDebug("Storing refresh token for user {UserId}, family {FamilyId}", userId, familyId);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenExpiryDays),
            CreatedByIp = ipAddress
        });
    }

    private async Task RevokeFamilyAsync(Guid familyId, string? ipAddress, DateTime now, CancellationToken cancellationToken)
    {
        logger.LogDebug("Revoking all unrevoked tokens in family {FamilyId}", familyId);
        await db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RevokedAt, now)
                .SetProperty(r => r.RevokedByIp, ipAddress), cancellationToken);
    }

    private TokenResponse BuildTokenResponse(User user, string refreshToken)
    {
        logger.LogDebug("Issuing access and refresh tokens for user {UserId}", user.Id);
        return new(tokens.CreateAccessToken(user), refreshToken, "Bearer", _jwt.AccessTokenExpiryMinutes * 60);
    }

    private static AuthOutcome InvalidCredentials() =>
        new(AuthStatus.Invalid, Message: "Incorrect username or password.");

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Shortens an email for logs, e.g. "ashfaq@gmail.com" becomes "a***@gmail.com", so the full address is not written to the log.
    /// Values without an @ are masked as well.
    /// </summary>
    private static string MaskEmail(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "(empty)";

        var at = value.IndexOf('@');
        if (at <= 0)
            return value[..1] + "***";

        return value[..1] + "***" + value[at..];
    }
}
