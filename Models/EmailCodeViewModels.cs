using System.ComponentModel.DataAnnotations;

namespace NeetBee.Web.Models;

/// <summary>Step 1 of email-code sign-in: the address to send the code to.</summary>
public sealed class EmailCodeViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Step 2 of email-code sign-in: the address and the 6-digit code from the email.</summary>
public sealed class VerifyCodeViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your email.")]
    public string Code { get; set; } = string.Empty;
}
