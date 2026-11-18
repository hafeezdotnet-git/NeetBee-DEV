namespace AuthenticationService.Entities;

/// <summary>A one-time sign-in code sent by email. Only a keyed hash of the code is stored, never the code.</summary>
public sealed class EmailOtpCode
{
    public long Id { get; set; }

    /// <summary>Stored lower-case.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 (hex) of "email:code", keyed with the JWT signing key.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Wrong guesses so far. The code stops working after the limit in AuthService.</summary>
    public int Attempts { get; set; }

    /// <summary>Set when the code is used. A used code cannot be used again.</summary>
    public DateTime? ConsumedAt { get; set; }
}
