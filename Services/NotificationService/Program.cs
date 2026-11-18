using System.Text.Json.Serialization;
using NotificationService.Configuration;
using NotificationService.Email;
using NeetBee.Common.Logging;
using NotificationService.Notifications;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (console + logs\notification-service-<date>.log, levels from appsettings "Serilog") ----
builder.AddNeetBeeLogging("notification-service");

// ---- Configuration -------------------------------------------------------------
builder.Services.Configure<NotificationApiOptions>(builder.Configuration.GetSection(NotificationApiOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<MessagingOptions>(builder.Configuration.GetSection(MessagingOptions.SectionName));

// ---- Email (SMTP, "Email" section) -----------------------------------------------
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// ---- SMS / WhatsApp provider ("Messaging:Provider": Stub or Twilio) ----------------
var messaging = builder.Configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();
if (messaging.IsTwilio)
{
    builder.Services.AddHttpClient<IMessagingProvider, TwilioMessagingProvider>(client =>
    {
        client.BaseAddress = new Uri(messaging.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
}
else
{
    builder.Services.AddSingleton<IMessagingProvider, StubMessagingProvider>();
}

builder.Services.AddScoped<INotificationService, NotificationDispatcher>();

// ---- Web / API ----------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "NEET BEE Notification Service", Version = "v1" }));

var app = builder.Build();

// Correlation ID: reuse the caller's X-Correlation-Id, or create one, so a request can be followed across services.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
        correlationId = Guid.NewGuid().ToString("N");

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
        Detail = app.Environment.IsDevelopment() ? error?.Message : "An unexpected error occurred."
    });
}));
app.UseStatusCodePages();
app.MapControllers();

app.Run();
