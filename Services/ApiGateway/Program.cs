using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ---- Authentication mode and JWT settings (must match AuthenticationService) ----
var authType = builder.Configuration["Authentication:Auth_type"] ?? "TokenBased";
var tokenBased = string.Equals(authType, "TokenBased", StringComparison.OrdinalIgnoreCase);
if (!tokenBased && !string.Equals(authType, "Existing", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Authentication:Auth_type must be 'TokenBased' or 'Existing'.");

var issuer = builder.Configuration["Authentication:Jwt:Issuer"] ?? string.Empty;
var audience = builder.Configuration["Authentication:Jwt:Audience"] ?? string.Empty;
var signingKey = builder.Configuration["Authentication:Jwt:SigningKey"] ?? string.Empty;

if (tokenBased && signingKey.Length < 32)
    throw new InvalidOperationException(
        "Authentication:Jwt:SigningKey must be at least 32 characters. Set it with the environment variable Authentication__Jwt__SigningKey.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "unique_name"
        };
    });

// YARP routes reference the policy named "Default". TokenBased: a valid JWT is required.
// Existing: no token check, so the legacy flow is unaffected.
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = tokenBased
        ? new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build()
        : new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
});

// ---- Rate limiting (per client IP) -----------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 50,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---- CORS -------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Correlation-Id")));

// ---- Reverse proxy (YARP) -----------------------------------------------------------
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms => transforms.AddRequestTransform(context =>
    {
        if (context.HttpContext.Items["CorrelationId"] is string correlationId)
            context.ProxyRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        return ValueTask.CompletedTask;
    }));

builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/authentication/swagger.json", "NEET BEE Authentication Service");
    options.SwaggerEndpoint("/swagger/payments/swagger.json", "NEET BEE Payment Service");
    options.SwaggerEndpoint("/swagger/notifications/swagger.json", "NEET BEE Notification Service");
    options.RoutePrefix = "swagger";
});

// Correlation ID: reuse the caller's header if sane, otherwise create one. Echoed in the response.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
        correlationId = Guid.NewGuid().ToString("N");

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

// Log every unhandled exception with its details, so the cause appears in this service's console window.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(error, "Unhandled error for {Method} {Path}: {Message}",
        context.Request.Method, context.Request.Path, error?.Message);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
    {
        Title = "Server error",
        Status = StatusCodes.Status500InternalServerError,
        // The exact message is returned only in Development. Other environments get a generic message.
        Detail = app.Environment.IsDevelopment() ? error?.Message : "An unexpected error occurred."
    });
}));
app.UseStatusCodePages();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Only routes defined in appsettings (ReverseProxy section) are reachable through the gateway.
app.MapReverseProxy();

app.Run();
