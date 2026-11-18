using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentService.Payments;

/// <summary>
/// Optional fields shared by all payment requests.
/// NotifyPhone: when set (E.164, e.g. +919876543210), the success/failure message also goes by SMS or WhatsApp.
/// </summary>
public abstract record PaymentRequestBase
{
    [Required, Range(typeof(decimal), "1", "10000000")]
    public decimal Amount { get; init; }

    [StringLength(200)]
    public string? Description { get; init; }

    [RegularExpression(@"^\+[1-9]\d{7,14}$")]
    public string? NotifyPhone { get; init; }

    public MessageChannelChoice NotifyChannel { get; init; } = MessageChannelChoice.Sms;
}

/// <summary>Sent as "Sms" or "WhatsApp".</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MessageChannelChoice
{
    Sms,
    WhatsApp
}

/// <summary>Net banking: the bank's short code from the gateway's bank list, e.g. "HDFC", "SBIN".</summary>
public sealed record NetBankingPaymentRequest : PaymentRequestBase
{
    [Required, RegularExpression(@"^[A-Za-z0-9_]{2,20}$")]
    public string BankCode { get; init; } = string.Empty;
}

/// <summary>UPI: the payer's UPI ID (VPA), e.g. "name@okhdfcbank".</summary>
public sealed record UpiPaymentRequest : PaymentRequestBase
{
    [Required, RegularExpression(@"^[A-Za-z0-9._-]{2,256}@[A-Za-z]{2,64}$")]
    public string UpiId { get; init; } = string.Empty;
}

/// <summary>
/// Card: the token the gateway's checkout returned after the user typed their card there. The full card number
/// and CVV never reach this service. Last4 and Network are for display only.
/// </summary>
public sealed record CardPaymentRequest : PaymentRequestBase
{
    [Required, StringLength(256, MinimumLength = 4)]
    public string CardToken { get; init; } = string.Empty;

    [RegularExpression(@"^\d{4}$")]
    public string? Last4 { get; init; }

    [RegularExpression(@"^[A-Za-z]{2,20}$")]
    public string? Network { get; init; }
}

/// <summary>One row of transaction history, and the response to a make-payment call.</summary>
public sealed record PaymentTransactionDto(
    long Id,
    decimal Amount,
    string Currency,
    string Method,
    string Status,
    string? Description,
    string? MethodDetail,
    string Gateway,
    string? GatewayReference,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public sealed record PaymentHistoryPage(
    IReadOnlyList<PaymentTransactionDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
