using NeetBee.Common.Notifications;

namespace NotificationService.Notifications;

/// <summary>
/// An SMS / WhatsApp provider. Selected by "Messaging:Provider" in appsettings.json.
/// Add a new class for another provider (e.g. MSG91, Gupshup) and register it in Program.cs.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>Sends <paramref name="text"/> to an E.164 phone number. Returns false when it could not be sent.</summary>
    Task<bool> SendAsync(string phoneNumber, NotificationChannel channel, string text, CancellationToken cancellationToken);
}
