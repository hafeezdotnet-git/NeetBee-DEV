using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace PaymentService.Notifications;

/// <summary>Where NotificationService is ("NotificationService" section) and the key it expects in X-Api-Key.</summary>
public sealed class NotificationClientOptions
{
    public const string SectionName = "NotificationService";

    public string BaseUrl { get; set; } = "http://localhost:5002/";

    /// <summary>Must match NotificationService's NotificationApi:ApiKey. Set via NotificationService__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Template values sent to NotificationService. Same JSON shape as its NotificationData.</summary>
public sealed record NotificationValues
{
    public string? Name { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Reference { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Calls NotificationService (POST api/notifications). Never throws for delivery problems; returns false.</summary>
public interface INotificationClient
{
    /// <param name="channel">"Email", "Sms" or "WhatsApp".</param>
    /// <param name="type">A NotificationService type, e.g. "PaymentSuccess".</param>
    Task<bool> SendAsync(string channel, string recipient, string type, NotificationValues data, CancellationToken cancellationToken);
}

public sealed class NotificationClient(
    HttpClient http,
    IOptions<NotificationClientOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<NotificationClient> logger) : INotificationClient
{
    public async Task<bool> SendAsync(string channel, string recipient, string type, NotificationValues data, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/notifications")
        {
            Content = JsonContent.Create(new { channel, type, recipient, data })
        };

        if (!string.IsNullOrEmpty(options.Value.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", options.Value.ApiKey);
        if (httpContextAccessor.HttpContext?.Items["CorrelationId"] is string correlationId)
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("NotificationService answered {Status} for a {Type} {Channel} notification", (int)response.StatusCode, type, channel);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogError(ex, "NotificationService could not be reached for a {Type} {Channel} notification", type, channel);
            return false;
        }
    }
}
