namespace AuthenticationService.Entities;

public sealed class RefreshToken
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 hex of the raw token. The raw token is returned to the client once and never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Shared by every token issued in one login chain. Used to revoke the whole chain.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one during rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public string? RevokedByIp { get; set; }
}
