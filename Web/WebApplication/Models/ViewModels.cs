using System.ComponentModel.DataAnnotations;

namespace NeetBee.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email or username.")]
    [Display(Name = "Email or username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the characters shown.")]
    public string CaptchaAnswer { get; set; } = string.Empty;

    public string CaptchaId { get; set; } = string.Empty;
    public string CaptchaImage { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Choose a username.")]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Use letters, numbers, dots, dashes or underscores.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Create a password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class DashboardViewModel
{
    public string UserName { get; set; } = string.Empty;

    /// <summary>Learning progress for the signed-in student (from IStudentProgressService).</summary>
    public StudentProgress Progress { get; set; } = StudentProgress.Empty;
}
