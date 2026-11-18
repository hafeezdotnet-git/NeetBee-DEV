namespace NeetBee.Web.Services;

/// <summary>
/// Adds the current web request's correlation ID to every call the web app makes to the gateway.
/// The gateway and the authentication service reuse that ID, so one ID links the web, gateway and service log lines.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.Items["CorrelationId"] as string;
        if (!string.IsNullOrEmpty(correlationId) && !request.Headers.Contains(HeaderName))
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);

        return base.SendAsync(request, cancellationToken);
    }
}
