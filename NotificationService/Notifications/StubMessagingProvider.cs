namespace NotificationService.Notifications;

/// <summary>
/// Used when no SMS / WhatsApp provider is configured (Messaging:Provider = "Stub"). Sends nothing.
/// In Development it writes the message to the log and reports success, so flows can be tested without a provider.
/// The log line can contain an OTP, so it never runs outside Development.
/// </summary>
public sealed class StubMessagingProvider(IHostEnvironment environment, ILogger<StubMessagingProvider> logger) : IMessagingProvider
{
    public Task<bool> SendAsync(string phoneNumber, NotificationChannel channel, string text, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            logger.LogWarning("Messaging is not configured. Development only: {Channel} to {To}: {Text}", channel, phoneNumber, text);
            return Task.FromResult(true);
        }

        logger.LogError("Messaging is not configured. Set Messaging:Provider and its settings to send {Channel} messages.", channel);
        return Task.FromResult(false);
    }
}
