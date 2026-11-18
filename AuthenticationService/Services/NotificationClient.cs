using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AuthenticationService.Services;

/// <summary>Where NotificationService is ("NotificationService" section) and the key it expects in X-Api-Key.</summary>
public sealed class NotificationClientOptions
{
    public const string SectionName = "NotificationService";

    public string BaseUrl { get; set; } = "http://localhost:5002/";

    /// <summary>Must match NotificationService's NotificationApi:ApiKey. Set via NotificationService__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Sends the auth service's messages through NotificationService (POST api/notifications).</summary>
public interface INotificationClient
{
    /// <summary>Emails a sign-in code. Returns false when it could not be sent; never throws for delivery problems.</summary>
    Task<bool> SendOtpEmailAsync(string email, string code, int expiresInMinutes, CancellationToken cancellationToken);
}

public sealed class NotificationClient(
    HttpClient http,
    IOptions<NotificationClientOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<NotificationClient> logger) : INotificationClient
{
    public async Task<bool> SendOtpEmailAsync(string email, string code, int expiresInMinutes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/notifications")
        {
            Content = JsonContent.Create(new
            {
                channel = "Email",
                type = "Otp",
                recipient = email,
                data = new { code, expiresInMinutes }
            })
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

            logger.LogWarning("NotificationService answered {Status} for a sign-in code email", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogError(ex, "NotificationService could not be reached for a sign-in code email");
            return false;
        }
    }
}
