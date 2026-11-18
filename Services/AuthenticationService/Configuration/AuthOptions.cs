namespace AuthenticationService.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";
    public const string TokenBasedMode = "TokenBased";
    public const string ExistingMode = "Existing";

    /// <summary>Selects the authentication mode: "TokenBased" or "Existing".</summary>
    public string Auth_type { get; set; } = TokenBasedMode;

    public JwtOptions Jwt { get; set; } = new();

    public bool IsTokenBased => string.Equals(Auth_type, TokenBasedMode, StringComparison.OrdinalIgnoreCase);

    public bool IsExisting => string.Equals(Auth_type, ExistingMode, StringComparison.OrdinalIgnoreCase);
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Set via environment variable Authentication__Jwt__SigningKey. Never commit a real value.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenExpiryMinutes { get; set; } = 15;

    public int RefreshTokenExpiryDays { get; set; } = 30;
}
