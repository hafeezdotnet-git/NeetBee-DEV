using System.ComponentModel.DataAnnotations;

namespace AuthenticationService.Models;

public sealed record RegisterRequest(
    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] string UserName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

/// <summary>Username or email plus password.</summary>
public sealed record LoginRequest(
    [Required, StringLength(256, MinimumLength = 3)] string Username,
    [Required, StringLength(128, MinimumLength = 1)] string Password,
    [Required, StringLength(64, MinimumLength = 32)] string CaptchaId,
    [Required, StringLength(12, MinimumLength = 4)] string CaptchaAnswer);

public sealed record RefreshRequest(
    [Required, StringLength(512, MinimumLength = 40)] string RefreshToken);

/// <summary>Request a sign-in code to be emailed to this address.</summary>
public sealed record EmailCodeRequest(
    [Required, EmailAddress, StringLength(256)] string Email);

/// <summary>An email address and the 6-digit code that was emailed to it.</summary>
public sealed record VerifyCodeRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(@"^\d{6}$")] string Code);

/// <summary>Google access token obtained by the web app after the user approved sign-in with Gmail.</summary>
public sealed record GoogleSignInRequest(
    [Required, StringLength(2048, MinimumLength = 10)] string AccessToken);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn);

public enum AuthStatus
{
    Ok,
    Invalid,
    Expired,
    Revoked,
    ReuseDetected,
    Conflict,
    ValidationFailed,
    RateLimited
}

public sealed record AuthOutcome(
    AuthStatus Status,
    TokenResponse? Tokens = null,
    int? UserId = null,
    string? Message = null);
