namespace AuthenticationService.CaptchaService;

/// <summary>Public challenge returned to the login form. The answer is never included.</summary>
public sealed record CaptchaModel(string Id, string ImageData);

internal sealed record CaptchaChallenge(string Answer, DateTimeOffset ExpiresAt);
