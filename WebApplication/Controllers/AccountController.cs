using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeetBee.Web.Models;
using NeetBee.Web.Services;

namespace NeetBee.Web.Controllers;

public sealed class AccountController(GatewayClient gateway, IConfiguration configuration, ILogger<AccountController> logger) : Controller
{
    /// <summary>Name of the temporary cookie that holds the Google sign-in result.</summary>
    public const string ExternalScheme = "External";

    /// <summary>True when a Google client ID and secret are configured.</summary>
    private bool GoogleEnabled =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        logger.LogInformation("GET Account/Login requested");

        if (User.Identity?.IsAuthenticated == true)
        {
            logger.LogDebug("Login page skipped: user is already signed in");
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        logger.LogInformation("POST Account/Login for user {Username}", model.Username);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Login form is invalid for user {Username}", model.Username);
            return View(model);
        }

        var signIn = await gateway.LoginAsync(model.Username.Trim(), model.Password, ct);
        if (!signIn.Ok || signIn.Value is null)
        {
            logger.LogWarning("Login failed at gateway for user {Username}: {Error}", model.Username, signIn.Error);
            ModelState.AddModelError(string.Empty, signIn.Error ?? "Sign-in failed. Please try again.");
            return View(model);
        }

        var me = await gateway.MeAsync(signIn.Value.AccessToken, ct);
        if (!me.Ok || me.Value is null)
        {
            logger.LogWarning("Login for user {Username} succeeded but the identity lookup failed: {Error}", model.Username, me.Error);
            ModelState.AddModelError(string.Empty, me.Error ?? "Sign-in failed. Please try again.");
            return View(model);
        }

        await SignInAsync(me.Value, signIn.Value);
        logger.LogInformation("Login completed for user {UserName}", me.Value.UserName);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        logger.LogInformation("GET Account/Register requested");

        if (User.Identity?.IsAuthenticated == true)
        {
            logger.LogDebug("Register page skipped: user is already signed in");
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        logger.LogInformation("POST Account/Register for user {UserName}", model.UserName);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Register form is invalid for user {UserName}", model.UserName);
            return View(model);
        }

        var result = await gateway.RegisterAsync(
            model.UserName.Trim(), model.Email.Trim(), model.Password, ct);

        if (!result.Ok)
        {
            logger.LogWarning("Register failed at gateway for user {UserName}: {Error}", model.UserName, result.Error);
            ModelState.AddModelError(string.Empty, result.Error ?? "Account could not be created.");
            return View(model);
        }

        logger.LogInformation("Account created for user {UserName}", model.UserName);
        TempData["Message"] = "Your account has been created. Please sign in.";
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Step 1 of email-code sign-in: asks for an email address.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult EmailCode()
    {
        logger.LogInformation("GET Account/EmailCode requested");

        if (User.Identity?.IsAuthenticated == true)
        {
            logger.LogDebug("Email code page skipped: user is already signed in");
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new EmailCodeViewModel());
    }

    /// <summary>Step 1 (submit): emails a 6-digit code, then moves on to the code entry page.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailCode(EmailCodeViewModel model, CancellationToken ct)
    {
        logger.LogInformation("POST Account/EmailCode for {Email}", MaskEmail(model.Email));

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Email code form is invalid for {Email}", MaskEmail(model.Email));
            return View(model);
        }

        var email = model.Email.Trim();
        var sent = await gateway.RequestEmailCodeAsync(email, ct);
        if (!sent.Ok)
        {
            logger.LogWarning("Email code request failed at gateway for {Email}: {Error}", MaskEmail(email), sent.Error);
            ModelState.AddModelError(string.Empty, sent.Error ?? "The code could not be sent. Please try again.");
            return View(model);
        }

        // Kept in TempData rather than the URL, so the address is not shown in the address bar.
        TempData["OtpEmail"] = email;
        logger.LogInformation("Email code sent for {Email}, redirecting to code entry", MaskEmail(email));
        return RedirectToAction(nameof(VerifyCode));
    }

    /// <summary>Step 2 of email-code sign-in: asks for the 6-digit code that was emailed.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyCode()
    {
        logger.LogInformation("GET Account/VerifyCode requested");

        if (User.Identity?.IsAuthenticated == true)
        {
            logger.LogDebug("Verify code page skipped: user is already signed in");
            return RedirectToAction("Index", "Dashboard");
        }

        if (TempData["OtpEmail"] is not string email || string.IsNullOrEmpty(email))
        {
            logger.LogWarning("Verify code page has no pending email; redirecting to EmailCode");
            return RedirectToAction(nameof(EmailCode));
        }

        TempData.Keep("OtpEmail");
        return View(new VerifyCodeViewModel { Email = email });
    }

    /// <summary>
    /// Step 2 (submit): checks the code and signs the user in. The account is created on the first successful sign-in.
    /// The code is never logged.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyCode(VerifyCodeViewModel model, CancellationToken ct)
    {
        logger.LogInformation("POST Account/VerifyCode for {Email}", MaskEmail(model.Email));

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Verify code form is invalid for {Email}", MaskEmail(model.Email));
            return View(model);
        }

        var signIn = await gateway.VerifyEmailCodeAsync(model.Email.Trim(), model.Code.Trim(), ct);
        if (!signIn.Ok || signIn.Value is null)
        {
            logger.LogWarning("Email code verification failed at gateway for {Email}: {Error}", MaskEmail(model.Email), signIn.Error);
            ModelState.AddModelError(string.Empty, signIn.Error ?? "The code is incorrect or has expired.");
            return View(model);
        }

        var me = await gateway.MeAsync(signIn.Value.AccessToken, ct);
        if (!me.Ok || me.Value is null)
        {
            logger.LogWarning("Email code sign-in for {Email} succeeded but the identity lookup failed: {Error}", MaskEmail(model.Email), me.Error);
            ModelState.AddModelError(string.Empty, me.Error ?? "Sign-in failed. Please try again.");
            return View(model);
        }

        await SignInAsync(me.Value, signIn.Value);
        TempData.Remove("OtpEmail");
        logger.LogInformation("Email code sign-in completed for user {UserName}", me.Value.UserName);
        return RedirectToAction("Index", "Dashboard");
    }

    /// <summary>
    /// Starts "Login with Gmail" or "Register with Gmail". Sends the user to Google to approve access.
    /// Both buttons use this action, because a Gmail sign-in creates the account on first use.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GoogleLogin(string? returnUrl = null)
    {
        logger.LogInformation("GET Account/GoogleLogin requested");

        if (!GoogleEnabled)
        {
            logger.LogWarning("Gmail sign-in requested but Authentication:Google:ClientId or ClientSecret is not configured");
            TempData["Error"] = "Gmail sign-in is not set up yet. Please use your username and password.";
            return RedirectToAction(nameof(Login));
        }

        var callbackUrl = Url.Action(nameof(GoogleResponse), new { returnUrl });
        logger.LogDebug("Redirecting to Google sign-in with callback {CallbackUrl}", callbackUrl);
        return Challenge(new AuthenticationProperties { RedirectUri = callbackUrl }, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Google sends the user back here after they approve or cancel the Gmail sign-in.
    /// The Google access token goes to the auth service, which asks Google who it belongs to and signs the user in.
    /// The access token is never logged.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleResponse(string? returnUrl, CancellationToken ct)
    {
        logger.LogInformation("GET Account/GoogleResponse received from Google");

        var external = await HttpContext.AuthenticateAsync(ExternalScheme);
        await HttpContext.SignOutAsync(ExternalScheme); // the temporary cookie is no longer needed

        var accessToken = external.Succeeded ? external.Properties?.GetTokenValue("access_token") : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogWarning("Gmail sign-in returned no access token (cancelled or failed at Google): {Failure}", external.Failure?.Message);
            TempData["Error"] = "Gmail sign-in was cancelled or could not be completed.";
            return RedirectToAction(nameof(Login));
        }

        var signIn = await gateway.GoogleSignInAsync(accessToken, ct);
        if (!signIn.Ok || signIn.Value is null)
        {
            logger.LogWarning("Gmail sign-in failed at gateway: {Error}", signIn.Error);
            TempData["Error"] = signIn.Error ?? "Gmail sign-in failed. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        var me = await gateway.MeAsync(signIn.Value.AccessToken, ct);
        if (!me.Ok || me.Value is null)
        {
            logger.LogWarning("Gmail sign-in succeeded but the identity lookup failed: {Error}", me.Error);
            TempData["Error"] = me.Error ?? "Gmail sign-in failed. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        await SignInAsync(me.Value, signIn.Value);
        logger.LogInformation("Gmail sign-in completed for user {UserName}", me.Value.UserName);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        logger.LogInformation("POST Account/Logout for user {UserName}", User.Identity?.Name);

        var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await gateway.LogoutAsync(refreshToken, ct);
            logger.LogDebug("Refresh token revoked at gateway during logout");
        }
        else
        {
            logger.LogWarning("Logout for user {UserName} had no refresh token to revoke", User.Identity?.Name);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        logger.LogInformation("Logout completed for user {UserName}", User.Identity?.Name);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Writes the signed-in user's identity and tokens into the "neetbee.session" cookie.
    /// The access token and refresh token are stored in the cookie, not logged.
    /// </summary>
    private async Task SignInAsync(CurrentUser user, TokenResponse tokens)
    {
        logger.LogDebug("Writing session cookie for UserId {UserId}", user.Id);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName)
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow
        };
        properties.StoreTokens(SessionTokens.From(tokens, DateTimeOffset.UtcNow));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);

        logger.LogDebug("Session cookie written for UserId {UserId}", user.Id);
    }

    /// <summary>
    /// Shortens an email for logs, e.g. "ashfaq@gmail.com" becomes "a***@gmail.com".
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
