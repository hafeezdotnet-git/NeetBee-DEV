namespace NotificationService.Configuration;

/// <summary>
/// Outgoing email settings used to send notifications. Set Email:Password through an environment
/// variable or user secrets (Email__Password), never in source control.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP server host name. Leave empty to disable sending (Development then logs the message).</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>The address notification emails are sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "NEET BEE";
}
