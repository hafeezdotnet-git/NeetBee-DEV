using System.Net.Http.Headers;
using System.Text;
using NotificationService.Configuration;
using Microsoft.Extensions.Options;

namespace NotificationService.Notifications;

/// <summary>
/// Sends SMS and WhatsApp messages through Twilio's Messages API (Messaging:Provider = "Twilio").
/// WhatsApp uses the same API with "whatsapp:" in front of both numbers.
/// </summary>
public sealed class TwilioMessagingProvider(
    HttpClient http,
    IOptions<MessagingOptions> options,
    ILogger<TwilioMessagingProvider> logger) : IMessagingProvider
{
    public async Task<bool> SendAsync(string phoneNumber, NotificationChannel channel, string text, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var from = channel == NotificationChannel.WhatsApp ? settings.WhatsAppFrom : settings.SmsFrom;

        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogError("Twilio is selected but Messaging:AccountSid, Messaging:AuthToken or the {Channel} sender number is missing.", channel);
            return false;
        }

        var prefix = channel == NotificationChannel.WhatsApp ? "whatsapp:" : string.Empty;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = prefix + phoneNumber,
                ["From"] = prefix + from,
                ["Body"] = text
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogError("Twilio rejected the {Channel} message with status {Status}", channel, (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Twilio could not be reached for a {Channel} message", channel);
            return false;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Twilio timed out for a {Channel} message", channel);
            return false;
        }
    }
}
