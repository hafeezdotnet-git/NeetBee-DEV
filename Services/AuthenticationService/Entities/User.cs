namespace AuthenticationService.Entities;

public sealed class User
{
    public int Id { get; set; }

    /// <summary>Stored lower-case. Unique.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Stored lower-case. Unique.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// ASP.NET Core PasswordHasher (PBKDF2, salted) output. Never the plain password.
    /// Null for accounts created with Gmail (Google) sign-in, which have no password.
    /// </summary>
    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
