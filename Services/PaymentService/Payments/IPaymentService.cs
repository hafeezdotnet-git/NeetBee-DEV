namespace PaymentService.Payments;

/// <summary>
/// Takes payments through the configured <see cref="IPaymentGateway"/>, records every attempt in
/// dbo.PaymentTransactions, and emails (and optionally texts) the user the result.
/// </summary>
public interface IPaymentService
{
    Task<PaymentTransactionDto> MakeNetBankingPaymentAsync(int userId, NetBankingPaymentRequest request, CancellationToken cancellationToken);

    Task<PaymentTransactionDto> MakeUpiPaymentAsync(int userId, UpiPaymentRequest request, CancellationToken cancellationToken);

    Task<PaymentTransactionDto> MakeCardPaymentAsync(int userId, CardPaymentRequest request, CancellationToken cancellationToken);

    /// <summary>The user's payments, newest first. <paramref name="page"/> starts at 1; pageSize is capped at 100.</summary>
    Task<PaymentHistoryPage> GetTransactionHistoryAsync(int userId, int page, int pageSize, CancellationToken cancellationToken);
}
