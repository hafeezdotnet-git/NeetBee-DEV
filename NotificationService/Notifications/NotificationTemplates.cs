using System.Globalization;

namespace NotificationService.Notifications;

/// <summary>A rendered message: the email subject and body, and the short text used for SMS and WhatsApp.</summary>
public sealed record RenderedNotification(string Subject, string EmailBody, string ShortText);

/// <summary>
/// The text of every notification, one template per <see cref="NotificationType"/>.
/// Change wording here; callers only pass the type and the values.
/// </summary>
public static class NotificationTemplates
{
    public static RenderedNotification Render(NotificationType type, NotificationData data)
    {
        var greeting = string.IsNullOrWhiteSpace(data.Name) ? "Hello," : $"Hello {data.Name},";
        var amount = FormatAmount(data.Amount, data.Currency);
        var reference = string.IsNullOrWhiteSpace(data.Reference) ? "" : $" Reference: {data.Reference}.";

        switch (type)
        {
            case NotificationType.Otp:
            {
                var code = Require(data.Code, nameof(data.Code), type);
                var minutes = data.ExpiresInMinutes ?? 10;
                // Same wording as the original sign-in email in AuthService.
                return new RenderedNotification(
                    "Your NEET BEE sign-in code",
                    $"Your NEET BEE sign-in code is {code}. It expires in {minutes} minutes. "
                    + "If you did not ask for this code, you can ignore this email.",
                    $"{code} is your NEET BEE sign-in code. It expires in {minutes} minutes. Do not share it with anyone.");
            }

            case NotificationType.EnrollmentSuccess:
            {
                var course = string.IsNullOrWhiteSpace(data.CourseName) ? "your NEET BEE course" : data.CourseName;
                return new RenderedNotification(
                    "You are enrolled in " + course,
                    $"{greeting}\n\nYou are now enrolled in {course}. Sign in to NEET BEE to start learning.\n\nAll the best,\nTeam NEET BEE",
                    $"NEET BEE: you are enrolled in {course}. Sign in to start learning.");
            }

            case NotificationType.PaymentSuccess:
                return new RenderedNotification(
                    "Payment received",
                    $"{greeting}\n\nWe received your payment of {amount}.{reference}\n\nThank you,\nTeam NEET BEE",
                    $"NEET BEE: payment of {amount} received.{reference}");

            case NotificationType.PaymentFailed:
            {
                var reason = string.IsNullOrWhiteSpace(data.Reason) ? "" : $" Reason: {data.Reason.Trim().TrimEnd('.')}.";
                return new RenderedNotification(
                    "Payment failed",
                    $"{greeting}\n\nYour payment of {amount} did not go through.{reason}{reference}\n"
                    + "No money was taken; if your account was debited it will be refunded by your bank. Please try again.\n\nTeam NEET BEE",
                    $"NEET BEE: payment of {amount} failed.{reason} Please try again.");
            }

            case NotificationType.LearningStatusUpdate:
            {
                var status = Require(data.LearningStatus, nameof(data.LearningStatus), type);
                return new RenderedNotification(
                    "Your NEET BEE learning update",
                    $"{greeting}\n\n{status}\n\nKeep going,\nTeam NEET BEE",
                    $"NEET BEE: {status}");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No template for this notification type.");
        }
    }

    private static string FormatAmount(decimal? amount, string? currency)
    {
        if (amount is null)
            return "your amount";

        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.ToUpperInvariant();
        var value = amount.Value.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        return code == "INR" ? $"Rs. {value}" : $"{code} {value}";
    }

    private static string Require(string? value, string field, NotificationType type) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{type} notifications need {field}.", field)
            : value;
}
