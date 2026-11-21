using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AuthenticationService.Configuration;
using AuthenticationService.CaptchaService;
using AuthenticationService.Models;
using AuthenticationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuthenticationService.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth, CaptchaValidator captcha, IOptions<AuthOptions> options, ILogger<AuthController> logger) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Creates a user account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/register for user {UserName} from {IpAddress}", request.UserName, ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        var outcome = await auth.RegisterAsync(request, cancellationToken);
        logger.LogInformation("POST api/auth/register finished with status {Status}", outcome.Status);

        return outcome.Status switch
        {
            AuthStatus.Ok => StatusCode(StatusCodes.Status201Created, new { id = outcome.UserId }),
            AuthStatus.Conflict => Problem(detail: outcome.Message, statusCode: StatusCodes.Status409Conflict),
            _ => Problem(detail: outcome.Message, statusCode: StatusCodes.Status400BadRequest)
        };
    }

    /// <summary>Validates credentials and returns an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/login from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        if (!captcha.ValidateAndConsume(request.CaptchaId, request.CaptchaAnswer))
            return Problem(detail: "The CAPTCHA is incorrect or expired. Please try again.", statusCode: StatusCodes.Status400BadRequest, title: "CAPTCHA verification failed");

        var outcome = await auth.LoginAsync(request, ClientIp, cancellationToken);
        logger.LogInformation("POST api/auth/login finished with status {Status}", outcome.Status);

        return outcome.Tokens is { } tokens
            ? Ok(tokens)
            : Problem(detail: outcome.Message, statusCode: StatusCodes.Status401Unauthorized, title: "Login failed");
    }

    /// <summary>Rotates a refresh token and returns a new access token and refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/refresh from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        var outcome = await auth.RefreshAsync(request.RefreshToken, ClientIp, cancellationToken);
        logger.LogInformation("POST api/auth/refresh finished with status {Status}", outcome.Status);

        return outcome.Tokens is { } tokens
            ? Ok(tokens)
            : Problem(detail: outcome.Message, statusCode: StatusCodes.Status401Unauthorized, title: "Refresh failed");
    }

    /// <summary>
    /// Emails a 6-digit sign-in code. Returns 202 when the email is sent. The same code signs in or creates the account.
    /// </summary>
    [HttpPost("otp/request")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestEmailCode([FromBody] EmailCodeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/otp/request from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        var outcome = await auth.RequestEmailCodeAsync(request.Email, cancellationToken);
        logger.LogInformation("POST api/auth/otp/request finished with status {Status}", outcome.Status);

        return outcome.Status switch
        {
            AuthStatus.Ok => Accepted(),
            AuthStatus.RateLimited => Problem(detail: outcome.Message, statusCode: StatusCodes.Status429TooManyRequests, title: "Please wait"),
            _ => Problem(detail: outcome.Message, statusCode: StatusCodes.Status400BadRequest, title: "Code not sent")
        };
    }

    /// <summary>
    /// Checks an emailed 6-digit code and signs the user in, creating the account on first use.
    /// The code is never logged.
    /// </summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyEmailCode([FromBody] VerifyCodeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/otp/verify from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        var outcome = await auth.VerifyEmailCodeAsync(request.Email, request.Code, ClientIp, cancellationToken);
        logger.LogInformation("POST api/auth/otp/verify finished with status {Status}", outcome.Status);

        if (outcome.Tokens is { } tokens)
            return Ok(tokens);

        return outcome.Status == AuthStatus.Conflict
            ? Problem(detail: outcome.Message, statusCode: StatusCodes.Status409Conflict, title: "Sign-in failed")
            : Problem(detail: outcome.Message, statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed");
    }

    /// <summary>
    /// Signs in with a Gmail account, creating the account on first use. This single endpoint serves both
    /// "Login with Gmail" and "Register with Gmail" on the web app. The auth service asks Google to verify the token.
    /// The Google access token is never logged.
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Google([FromBody] GoogleSignInRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/google from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        var outcome = await auth.GoogleSignInAsync(request.AccessToken, ClientIp, cancellationToken);
        logger.LogInformation("POST api/auth/google finished with status {Status}", outcome.Status);

        if (outcome.Tokens is { } tokens)
            return Ok(tokens);

        return outcome.Status == AuthStatus.Conflict
            ? Problem(detail: outcome.Message, statusCode: StatusCodes.Status409Conflict, title: "Google sign-in failed")
            : Problem(detail: outcome.Message, statusCode: StatusCodes.Status401Unauthorized, title: "Google sign-in failed");
    }

    /// <summary>Revokes the refresh token's family. Always returns 204 for well-formed requests.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("POST api/auth/logout from {IpAddress}", ClientIp);

        if (!options.Value.IsTokenBased)
            return LegacyModeUnavailable();

        await auth.LogoutAsync(request.RefreshToken, ClientIp, cancellationToken);
        logger.LogInformation("POST api/auth/logout completed");
        return NoContent();
    }

    /// <summary>Returns the identity from a valid access token (protected endpoint).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var userName = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName);
        logger.LogDebug("GET api/auth/me for UserId {UserId}", id);

        return Ok(new { id, userName });
    }

    private ObjectResult LegacyModeUnavailable()
    {
        logger.LogWarning("Request rejected: Auth_type is Existing, which has no implementation in this codebase");
        return Problem(
            detail: "Auth_type is 'Existing'. No legacy login implementation exists in this codebase.",
            statusCode: StatusCodes.Status501NotImplemented);
    }
}
