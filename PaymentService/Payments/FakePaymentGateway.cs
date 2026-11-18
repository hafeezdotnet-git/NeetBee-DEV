namespace PaymentService.Payments;

/// <summary>
/// Stand-in gateway for development and testing (Payments:Gateway = "Fake"). No money moves.
/// Every payment succeeds, except these test values, which fail so the failure path can be tried:
///   net banking bank code "FAIL", a UPI ID starting with "fail@" (e.g. fail@upi), card token "tok_fail".
/// </summary>
public sealed class FakePaymentGateway(ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public string Name => "fake";

    public Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken cancellationToken)
    {
        var reference = "fake_" + Guid.NewGuid().ToString("N")[..20];

        var fail = request.Method switch
        {
            PaymentMethod.NetBanking => string.Equals(request.BankCode, "FAIL", StringComparison.OrdinalIgnoreCase),
            PaymentMethod.Upi => request.UpiId?.StartsWith("fail@", StringComparison.OrdinalIgnoreCase) == true,
            PaymentMethod.Card => string.Equals(request.CardToken, "tok_fail", StringComparison.Ordinal),
            _ => false
        };

        logger.LogWarning("Fake payment gateway used for transaction {TransactionId}: no real money is charged", request.TransactionId);

        return Task.FromResult(fail
            ? new GatewayChargeResult(PaymentStatus.Failed, reference, "The payment was declined (test failure).")
            : new GatewayChargeResult(PaymentStatus.Success, reference));
    }
}
