namespace PaymentService.Entities;

/// <summary>The columns of dbo.Users this service reads. Never written here; AuthenticationService owns users.</summary>
public sealed class PaymentUser
{
    public int Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
