using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PaymentService.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PaymentService.Controllers;

/// <summary>
/// Payments for the signed-in user. Every endpoint needs a valid access token; the user is taken from the token,
/// never from the request body. A declined payment still returns 200 with Status "failed".
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public sealed class PaymentsController(IPaymentService payments, ILogger<PaymentsController> logger) : ControllerBase
{
    /// <summary>Pays by net banking.</summary>
    [HttpPost("netbanking")]
    [ProducesResponseType(typeof(PaymentTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> NetBanking([FromBody] NetBankingPaymentRequest request, CancellationToken cancellationToken) =>
        PayAsync("netbanking", userId => payments.MakeNetBankingPaymentAsync(userId, request, cancellationToken));

    /// <summary>Pays by UPI.</summary>
    [HttpPost("upi")]
    [ProducesResponseType(typeof(PaymentTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Upi([FromBody] UpiPaymentRequest request, CancellationToken cancellationToken) =>
        PayAsync("upi", userId => payments.MakeUpiPaymentAsync(userId, request, cancellationToken));

    /// <summary>Pays by card, using the token from the gateway's checkout. Never send a card number or CVV.</summary>
    [HttpPost("card")]
    [ProducesResponseType(typeof(PaymentTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Card([FromBody] CardPaymentRequest request, CancellationToken cancellationToken) =>
        PayAsync("card", userId => payments.MakeCardPaymentAsync(userId, request, cancellationToken));

    /// <summary>The signed-in user's payments, newest first.</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(PaymentHistoryPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized();

        logger.LogInformation("GET api/payments/history for UserId {UserId}, page {Page}", userId, page);
        return Ok(await payments.GetTransactionHistoryAsync(userId, page, pageSize, cancellationToken));
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private async Task<IActionResult> PayAsync(string method, Func<int, Task<PaymentTransactionDto>> pay)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized();

        logger.LogInformation("POST api/payments/{Method} for UserId {UserId}", method, userId);
        try
        {
            var result = await pay(userId);
            logger.LogInformation("POST api/payments/{Method} finished: transaction {TransactionId} {Status}", method, result.Id, result.Status);
            return Ok(result);
        }
        catch (PaymentRejectedException ex)
        {
            logger.LogWarning("POST api/payments/{Method} rejected for UserId {UserId}: {Reason}", method, userId, ex.Message);
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Payment not accepted");
        }
    }
}
