namespace PaymentService.Entities;

/// <summary>
/// One payment attempt (dbo.PaymentTransactions, created by Database/008_create_payment_transactions.sql).
/// Card numbers and CVV are never stored: only the last 4 digits and the card network, in <see cref="MethodDetail"/>.
/// </summary>
public sealed class PaymentTransaction
{
    public long Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Amount in major units (rupees), two decimals.</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code, upper-case. "INR".</summary>
    public string Currency { get; set; } = "INR";

    /// <summary>"netbanking", "upi" or "card".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>"pending", "success" or "failed".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>What the payment is for, e.g. "premium" or "NEET 2027 Premium". Shown in history.</summary>
    public string? Description { get; set; }

    /// <summary>Masked, display-only detail: bank code, masked UPI ID, or "VISA ****1234". Never a full card number.</summary>
    public string? MethodDetail { get; set; }

    /// <summary>Which gateway handled it, e.g. "fake" or "razorpay".</summary>
    public string Gateway { get; set; } = string.Empty;

    /// <summary>The gateway's payment ID. Null until the gateway answers.</summary>
    public string? GatewayReference { get; set; }

    /// <summary>A short reason safe to show the user when Status is "failed".</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>When the payment reached "success" or "failed".</summary>
    public DateTime? CompletedAt { get; set; }
}
