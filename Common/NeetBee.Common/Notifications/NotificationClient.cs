using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeetBee.Common.Notifications;

/// <summary>Where NotificationService is ("NotificationService" section) and the key it expects in X-Api-Key.</summary>
public sealed class NotificationClientOptions
{
    public const string SectionName = "NotificationService";

    public string BaseUrl { get; set; } = "http://localhost:5002/";

    /// <summary>Must match NotificationService's NotificationApi:ApiKey. Set via NotificationService__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Sends notifications through NotificationService (POST api/notifications).</summary>
public interface INotificationClient
{
    /// <summary>
    /// Sends one notification. NotificationService owns the wording (one template per <see cref="NotificationType"/>).
    /// Returns false when it could not be sent; never throws for delivery problems.
    /// </summary>
    /// <param name="recipient">An email address for <see cref="NotificationChannel.Email"/>, an E.164 phone number for Sms and WhatsApp.</param>
    Task<bool> SendAsync(NotificationChannel channel, string recipient, NotificationType type, NotificationData data, CancellationToken cancellationToken);
}

public sealed class NotificationClient(
    HttpClient http,
    IOptions<NotificationClientOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<NotificationClient> logger) : INotificationClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<bool> SendAsync(NotificationChannel channel, string recipient, NotificationType type, NotificationData data, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/notifications")
        {
            Content = JsonContent.Create(new SendNotificationRequest(channel, type, recipient, data), options: Json)
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

public static class NotificationClientRegistration
{
    /// <summary>Registers <see cref="INotificationClient"/>, configured from the "NotificationService" section.</summary>
    public static IServiceCollection AddNeetBeeNotificationClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NotificationClientOptions.SectionName);
        services.Configure<NotificationClientOptions>(section);
        var baseUrl = section["BaseUrl"] ?? "http://localhost:5002/";

        services.AddHttpContextAccessor();
        services.AddHttpClient<INotificationClient, NotificationClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        return services;
    }
}
