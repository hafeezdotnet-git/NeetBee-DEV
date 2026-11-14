using AuthenticationService.Models;

namespace AuthenticationService.Services;

public interface IAuthService
{
    Task<AuthOutcome> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthOutcome> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthOutcome> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Signs in with a Gmail (Google) account, or creates one on first use. Used by "Login with Gmail" and "Register with Gmail".</summary>
    Task<AuthOutcome> GoogleSignInAsync(string accessToken, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Emails a 6-digit sign-in code. Used by "Sign in with an email code" and "Sign up with an email code".</summary>
    Task<AuthOutcome> RequestEmailCodeAsync(string email, CancellationToken cancellationToken);

    /// <summary>Checks an emailed code and signs the user in, creating the account on first use.</summary>
    Task<AuthOutcome> VerifyEmailCodeAsync(string email, string code, string? ipAddress, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
}
