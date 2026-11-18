namespace PaymentService.Payments;

public enum PaymentMethod
{
    NetBanking,
    Upi,
    Card
}

public enum PaymentStatus
{
    Pending,
    Success,
    Failed
}

/// <summary>
/// What the gateway needs to charge one payment. Only one of the method fields is set, matching <see cref="Method"/>.
/// There is no card number or CVV field on purpose: card details are entered in the gateway's own checkout
/// (e.g. Razorpay Checkout) and only its token reaches this service.
/// </summary>
public sealed record GatewayChargeRequest(
    long TransactionId,
    int UserId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Description,
    string? BankCode = null,
    string? UpiId = null,
    string? CardToken = null);

/// <summary>The gateway's answer. <see cref="GatewayReference"/> is the gateway's own payment ID.</summary>
public sealed record GatewayChargeResult(
    PaymentStatus Status,
    string? GatewayReference,
    string? FailureReason = null);

/// <summary>
/// A payment provider. Selected by "Payments:Gateway" in appsettings.json. Ships with <see cref="FakePaymentGateway"/>;
/// add a RazorpayPaymentGateway (or similar) implementing this and register it in Program.cs.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Short lower-case name stored with each transaction, e.g. "fake" or "razorpay".</summary>
    string Name { get; }

    /// <summary>Charges the payment. Returns Failed (not an exception) when the provider declines it.</summary>
    Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken cancellationToken);
}
