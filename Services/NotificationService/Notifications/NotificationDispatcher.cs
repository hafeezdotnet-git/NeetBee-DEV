using System.Text.RegularExpressions;
using NotificationService.Email;

using NeetBee.Common.Notifications;

namespace NotificationService.Notifications;

/// <summary>Default <see cref="INotificationService"/>: renders the template and hands it to email or the messaging provider.</summary>
public sealed partial class NotificationDispatcher(
    IEmailSender emailSender,
    IMessagingProvider messagingProvider,
    ILogger<NotificationDispatcher> logger) : INotificationService
{
    public Task<bool> SendAsync(NotificationChannel channel, string recipient, NotificationType type, NotificationData data, CancellationToken cancellationToken) =>
        channel == NotificationChannel.Email
            ? SendEmailAsync(recipient, type, data, cancellationToken)
            : SendMessageAsync(recipient, channel, type, data, cancellationToken);

    public async Task<bool> SendEmailAsync(string toEmail, NotificationType type, NotificationData data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
        {
            logger.LogWarning("{Type} email not sent: recipient is not an email address", type);
            return false;
        }

        var message = NotificationTemplates.Render(type, data);
        var sent = await emailSender.SendAsync(toEmail.Trim(), message.Subject, message.EmailBody, cancellationToken);

        if (sent)
            logger.LogInformation("{Type} email sent to {To}", type, MaskEmail(toEmail));
        else
            logger.LogWarning("{Type} email to {To} could not be sent", type, MaskEmail(toEmail));

        return sent;
    }

    public async Task<bool> SendMessageAsync(string phoneNumber, NotificationChannel channel, NotificationType type, NotificationData data, CancellationToken cancellationToken)
    {
        if (channel == NotificationChannel.Email)
            throw new ArgumentException("Use SendEmailAsync for the Email channel.", nameof(channel));

        var phone = phoneNumber?.Trim() ?? string.Empty;
        if (!E164().IsMatch(phone))
        {
            logger.LogWarning("{Type} {Channel} message not sent: phone number is not in +<country><number> form", type, channel);
            return false;
        }

        var message = NotificationTemplates.Render(type, data);
        var sent = await messagingProvider.SendAsync(phone, channel, message.ShortText, cancellationToken);

        if (sent)
            logger.LogInformation("{Type} {Channel} message sent to {To}", type, channel, MaskPhone(phone));
        else
            logger.LogWarning("{Type} {Channel} message to {To} could not be sent", type, channel, MaskPhone(phone));

        return sent;
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    private static string MaskEmail(string value)
    {
        var at = value.IndexOf('@');
        return at <= 0 ? "***" : value[..1] + "***" + value[at..];
    }

    private static string MaskPhone(string value) =>
        value.Length <= 4 ? "***" : new string('*', value.Length - 4) + value[^4..];
}
