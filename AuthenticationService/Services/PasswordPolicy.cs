namespace AuthenticationService.Services;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <summary>Returns an error message when the password does not meet policy, otherwise null.</summary>
    public static string? Validate(string password)
    {
        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";

        if (password.Length > MaxLength)
            return $"Password must be at most {MaxLength} characters.";

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Password must contain at least one letter and one number.";

        return null;
    }
}
