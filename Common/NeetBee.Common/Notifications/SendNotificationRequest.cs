using System.ComponentModel.DataAnnotations;

namespace NeetBee.Common.Notifications;

/// <summary>
/// Request body for NotificationService's POST api/notifications. Enums are sent as strings, e.g.
/// { "channel": "Email", "type": "Otp", "recipient": "a@b.com", "data": { "code": "123456", "expiresInMinutes": 10 } }
/// </summary>
public sealed record SendNotificationRequest(
    [Required] NotificationChannel? Channel,
    [Required] NotificationType? Type,
    [Required, StringLength(256, MinimumLength = 3)] string Recipient,
    NotificationData? Data);
