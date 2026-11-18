namespace NotificationService.Configuration;

/// <summary>
/// Who may call this service ("NotificationApi" section). Callers (AuthenticationService, PaymentService) send the key
/// in the X-Api-Key header. Set it with the environment variable NotificationApi__ApiKey (same value in each caller's
/// NotificationService__ApiKey), never in source control.
/// </summary>
public sealed class NotificationApiOptions
{
    public const string SectionName = "NotificationApi";
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Shared key, 32+ characters. When empty: Development accepts calls without a key (with a warning);
    /// every other environment rejects all calls.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
