namespace NeetBee.Common.Notifications;

/// <summary>Every kind of message NEET BEE sends. NotificationService has a template for each type (NotificationTemplates.cs).</summary>
public enum NotificationType
{
    /// <summary>A one-time sign-in code. Needs <see cref="NotificationData.Code"/>.</summary>
    Otp,

    /// <summary>The user was enrolled in a course or plan. Uses <see cref="NotificationData.CourseName"/>.</summary>
    EnrollmentSuccess,

    /// <summary>A payment went through. Uses Amount, Currency and Reference.</summary>
    PaymentSuccess,

    /// <summary>A payment did not go through. Uses Amount, Currency, Reference and Reason.</summary>
    PaymentFailed,

    /// <summary>An update on the learner's progress. Uses <see cref="NotificationData.LearningStatus"/>.</summary>
    LearningStatusUpdate
}

/// <summary>How a notification is delivered. Any <see cref="NotificationType"/> can go over any channel.</summary>
public enum NotificationChannel
{
    /// <summary>Email through SMTP ("Email" settings). Recipient: an email address.</summary>
    Email,

    /// <summary>SMS through the messaging provider ("Messaging" settings). Recipient: E.164 phone number.</summary>
    Sms,

    /// <summary>WhatsApp through the messaging provider ("Messaging" settings). Recipient: E.164 phone number.</summary>
    WhatsApp
}

/// <summary>
/// Values that fill a template. Each type uses only some of them; the rest stay null.
/// Callers never write the message text themselves.
/// </summary>
public sealed record NotificationData
{
    /// <summary>The recipient's name or username, used in the greeting. Optional.</summary>
    public string? Name { get; init; }

    /// <summary>OTP: the code. Never logged.</summary>
    public string? Code { get; init; }

    /// <summary>OTP: how long the code stays valid.</summary>
    public int? ExpiresInMinutes { get; init; }

    /// <summary>Enrollment: course or plan name, e.g. "NEET 2027 Premium".</summary>
    public string? CourseName { get; init; }

    /// <summary>Payments: amount in major units (rupees).</summary>
    public decimal? Amount { get; init; }

    /// <summary>Payments: ISO currency code. Defaults to INR in the templates.</summary>
    public string? Currency { get; init; }

    /// <summary>Payments: the NEET BEE transaction number or gateway reference shown to the user.</summary>
    public string? Reference { get; init; }

    /// <summary>Payment failed: a short reason safe to show the user.</summary>
    public string? Reason { get; init; }

    /// <summary>Learning status: the update text, e.g. "You completed 40 of 50 Physics questions this week."</summary>
    public string? LearningStatus { get; init; }
}
