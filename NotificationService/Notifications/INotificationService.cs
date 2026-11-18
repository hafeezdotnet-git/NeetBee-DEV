namespace NotificationService.Notifications;

/// <summary>
/// Sends NEET BEE notifications. Pick a <see cref="NotificationType"/> and a <see cref="NotificationChannel"/> and pass
/// the values; the text comes from <see cref="NotificationTemplates"/>. Methods return false (and log why) when the
/// message could not be delivered; they do not throw for delivery failures.
/// </summary>
public interface INotificationService
{
    /// <summary>Sends over any channel: Email to an address, Sms / WhatsApp to an E.164 phone number.</summary>
    Task<bool> SendAsync(NotificationChannel channel, string recipient, NotificationType type, NotificationData data, CancellationToken cancellationToken);

    /// <summary>Sends by email through <c>IEmailSender</c> (SMTP, "Email" settings).</summary>
    Task<bool> SendEmailAsync(string toEmail, NotificationType type, NotificationData data, CancellationToken cancellationToken);

    /// <summary>
    /// Sends by SMS or WhatsApp through the configured <see cref="IMessagingProvider"/> ("Messaging" settings).
    /// <paramref name="phoneNumber"/> must be in E.164 form, e.g. +919876543210.
    /// </summary>
    Task<bool> SendMessageAsync(string phoneNumber, NotificationChannel channel, NotificationType type, NotificationData data, CancellationToken cancellationToken);
}
