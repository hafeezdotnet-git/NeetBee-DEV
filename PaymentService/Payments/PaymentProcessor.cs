using PaymentService.Configuration;
using PaymentService.Data;
using PaymentService.Entities;
using PaymentService.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace PaymentService.Payments;

/// <summary>Thrown for a payment request the service will not send to the gateway (bad amount, unknown user).</summary>
public sealed class PaymentRejectedException(string message) : Exception(message);

public sealed class PaymentProcessor(
    PaymentDbContext db,
    IPaymentGateway gateway,
    INotificationClient notifications,
    IOptions<PaymentOptions> options,
    ILogger<PaymentProcessor> logger) : IPaymentService
{
    private const string GatewayErrorReason = "The payment service could not be reached. Please try again.";

    public Task<PaymentTransactionDto> MakeNetBankingPaymentAsync(int userId, NetBankingPaymentRequest request, CancellationToken cancellationToken)
    {
        var bank = request.BankCode.Trim().ToUpperInvariant();
        return ProcessAsync(userId, request, PaymentMethod.NetBanking, bank,
            id => Charge(id, userId, request, PaymentMethod.NetBanking) with { BankCode = bank },
            cancellationToken);
    }

    public Task<PaymentTransactionDto> MakeUpiPaymentAsync(int userId, UpiPaymentRequest request, CancellationToken cancellationToken)
    {
        var upiId = request.UpiId.Trim().ToLowerInvariant();
        return ProcessAsync(userId, request, PaymentMethod.Upi, MaskUpi(upiId),
            id => Charge(id, userId, request, PaymentMethod.Upi) with { UpiId = upiId },
            cancellationToken);
    }

    public Task<PaymentTransactionDto> MakeCardPaymentAsync(int userId, CardPaymentRequest request, CancellationToken cancellationToken)
    {
        // Display-only: network and last 4 digits. The card number and CVV are never sent to or kept by this service.
        var detail = string.Join(' ', new[]
        {
            request.Network?.Trim().ToUpperInvariant(),
            request.Last4 is null ? null : "****" + request.Last4
        }.Where(p => !string.IsNullOrEmpty(p)));

        return ProcessAsync(userId, request, PaymentMethod.Card, detail.Length == 0 ? null : detail,
            id => Charge(id, userId, request, PaymentMethod.Card) with { CardToken = request.CardToken.Trim() },
            cancellationToken);
    }

    public async Task<PaymentHistoryPage> GetTransactionHistoryAsync(int userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.PaymentTransactions.AsNoTracking().Where(t => t.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        logger.LogDebug("Payment history for UserId {UserId}: page {Page}, {Count} of {Total}", userId, page, rows.Count, total);
        return new PaymentHistoryPage(rows.Select(ToDto).ToList(), page, pageSize, total);
    }

    private GatewayChargeRequest Charge(long transactionId, int userId, PaymentRequestBase request, PaymentMethod method) =>
        new(transactionId, userId, decimal.Round(request.Amount, 2), options.Value.Currency, method, request.Description?.Trim());

    /// <summary>
    /// Shared flow for every method: validate, store a pending row, call the gateway, store the result, notify the user.
    /// A notification failure never changes the payment result.
    /// </summary>
    private async Task<PaymentTransactionDto> ProcessAsync(
        int userId,
        PaymentRequestBase request,
        PaymentMethod method,
        string? methodDetail,
        Func<long, GatewayChargeRequest> buildCharge,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var amount = decimal.Round(request.Amount, 2);
        if (amount <= 0 || amount > settings.MaxAmount)
            throw new PaymentRejectedException($"Amount must be between 1 and {settings.MaxAmount:0.##}.");

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.UserName, u.Email })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PaymentRejectedException("The account was not found or is not active.");

        var now = DateTime.UtcNow;
        var transaction = new PaymentTransaction
        {
            UserId = userId,
            Amount = amount,
            Currency = settings.Currency.ToUpperInvariant(),
            Method = ToCode(method),
            Status = ToCode(PaymentStatus.Pending),
            Description = request.Description?.Trim(),
            MethodDetail = methodDetail,
            Gateway = gateway.Name,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PaymentTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Payment {TransactionId} started: {Method} {Amount} {Currency} for UserId {UserId} via {Gateway}",
            transaction.Id, transaction.Method, amount, transaction.Currency, userId, gateway.Name);

        GatewayChargeResult result;
        try
        {
            result = await gateway.ChargeAsync(buildCharge(transaction.Id), cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Gateway {Gateway} failed for payment {TransactionId}", gateway.Name, transaction.Id);
            result = new GatewayChargeResult(PaymentStatus.Failed, null, GatewayErrorReason);
        }

        transaction.Status = ToCode(result.Status);
        transaction.GatewayReference = result.GatewayReference;
        transaction.FailureReason = result.Status == PaymentStatus.Failed ? Truncate(result.FailureReason ?? "Payment failed.", 400) : null;
        transaction.UpdatedAt = DateTime.UtcNow;
        transaction.CompletedAt = result.Status == PaymentStatus.Pending ? null : transaction.UpdatedAt;
        // Not cancellable: the gateway has already answered, so its result must be recorded.
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Payment {TransactionId} finished with status {Status}, gateway reference {Reference}",
            transaction.Id, transaction.Status, transaction.GatewayReference);

        await NotifyAsync(user.Email, user.UserName, request, transaction, result.Status);
        return ToDto(transaction);
    }

    private async Task NotifyAsync(string email, string userName, PaymentRequestBase request, PaymentTransaction transaction, PaymentStatus status)
    {
        if (status == PaymentStatus.Pending)
            return; // the final result arrives later (e.g. a gateway webhook); notify then

        // Sent to NotificationService over HTTP; it owns the wording of these messages.
        var type = status == PaymentStatus.Success ? "PaymentSuccess" : "PaymentFailed";
        var data = new NotificationValues
        {
            Name = userName,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Reference = $"NB{transaction.Id}",
            Reason = transaction.FailureReason
        };

        try
        {
            await notifications.SendAsync("Email", email, type, data, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.NotifyPhone))
            {
                var channel = request.NotifyChannel == MessageChannelChoice.WhatsApp ? "WhatsApp" : "Sms";
                await notifications.SendAsync(channel, request.NotifyPhone, type, data, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment {TransactionId}: {Type} notification failed", transaction.Id, type);
        }
    }

    private static PaymentTransactionDto ToDto(PaymentTransaction t) => new(
        t.Id, t.Amount, t.Currency, t.Method, t.Status, t.Description, t.MethodDetail,
        t.Gateway, t.GatewayReference, t.FailureReason, Utc(t.CreatedAt), Utc(t.UpdatedAt),
        t.CompletedAt is { } completed ? Utc(completed) : null);

    /// <summary>Values are stored in UTC; SQL Server returns them without a kind, so mark them UTC for JSON ("...Z").</summary>
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string ToCode(PaymentMethod method) => method switch
    {
        PaymentMethod.NetBanking => "netbanking",
        PaymentMethod.Upi => "upi",
        PaymentMethod.Card => "card",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static string ToCode(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "pending",
        PaymentStatus.Success => "success",
        PaymentStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>"ashfaq@okhdfc" becomes "as***@okhdfc".</summary>
    private static string MaskUpi(string upiId)
    {
        var at = upiId.IndexOf('@');
        var name = at < 0 ? upiId : upiId[..at];
        var handle = at < 0 ? string.Empty : upiId[at..];
        return (name.Length <= 2 ? name[..1] : name[..2]) + "***" + handle;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
