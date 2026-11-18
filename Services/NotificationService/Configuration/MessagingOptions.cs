namespace NotificationService.Configuration;

/// <summary>
/// SMS / WhatsApp provider settings ("Messaging" section). Set Messaging:AuthToken through an environment
/// variable or user secrets (Messaging__AuthToken), never in source control.
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";
    public const string StubProvider = "Stub";
    public const string TwilioProvider = "Twilio";

    /// <summary>"Stub" (default: nothing is sent; Development logs the text) or "Twilio".</summary>
    public string Provider { get; set; } = StubProvider;

    /// <summary>Twilio: Account SID.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio: Auth token. Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number for SMS, in E.164 form (e.g. +15005550006).</summary>
    public string SmsFrom { get; set; } = string.Empty;

    /// <summary>The sending number for WhatsApp, in E.164 form. Must be enabled for WhatsApp at the provider.</summary>
    public string WhatsAppFrom { get; set; } = string.Empty;

    /// <summary>Provider API base address. Twilio default: https://api.twilio.com/</summary>
    public string BaseUrl { get; set; } = "https://api.twilio.com/";

    public bool IsTwilio => string.Equals(Provider, TwilioProvider, StringComparison.OrdinalIgnoreCase);
}
