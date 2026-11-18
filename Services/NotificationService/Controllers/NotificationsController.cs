using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NotificationService.Configuration;
using NotificationService.Notifications;

using NeetBee.Common.Notifications;

namespace NotificationService.Controllers;

/// <summary>
/// Sends one notification. Internal endpoint for other NEET BEE services: it needs the shared key in X-Api-Key,
/// so a browser or end user cannot use it to send messages.
/// </summary>
[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(
    INotificationService notifications,
    IOptions<NotificationApiOptions> apiOptions,
    IHostEnvironment environment,
    ILogger<NotificationsController> logger) : ControllerBase
{
    /// <summary>Returns 202 when the message was handed to email / the messaging provider, 502 when delivery failed.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request, CancellationToken cancellationToken)
    {
        if (!CallerIsAllowed())
            return Problem(detail: "A valid X-Api-Key header is required.", statusCode: StatusCodes.Status401Unauthorized, title: "Not allowed");

        var channel = request.Channel!.Value;
        var type = request.Type!.Value;
        logger.LogInformation("POST api/notifications: {Type} by {Channel}", type, channel);

        bool sent;
        try
        {
            sent = await notifications.SendAsync(channel, request.Recipient, type, request.Data ?? new NotificationData(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // A template value is missing, e.g. an Otp without a code.
            logger.LogWarning("POST api/notifications rejected: {Reason}", ex.Message);
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Notification not sent");
        }

        return sent
            ? Accepted()
            : Problem(detail: $"The {channel} message could not be delivered.", statusCode: StatusCodes.Status502BadGateway, title: "Notification not sent");
    }

    private bool CallerIsAllowed()
    {
        var expected = apiOptions.Value.ApiKey;
        if (string.IsNullOrEmpty(expected))
        {
            if (environment.IsDevelopment())
            {
                logger.LogWarning("NotificationApi:ApiKey is not set. Development only: accepting the call without a key.");
                return true;
            }

            logger.LogError("NotificationApi:ApiKey is not set, so every call is rejected. Set NotificationApi__ApiKey.");
            return false;
        }

        var given = Request.Headers[NotificationApiOptions.HeaderName].FirstOrDefault() ?? string.Empty;
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
        if (!ok)
            logger.LogWarning("POST api/notifications rejected: missing or wrong X-Api-Key");
        return ok;
    }
}
