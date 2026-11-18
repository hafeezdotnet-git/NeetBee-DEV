using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PaymentService.Configuration;
using PaymentService.Data;
using NeetBee.Common.Logging;
using NeetBee.Common.Notifications;
using PaymentService.Payments;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (console + logs\payment-service-<date>.log, levels from appsettings "Serilog") ----
builder.AddNeetBeeLogging("payment-service");

// ---- Access tokens: issued by AuthenticationService, checked here with the same JWT settings ----
var issuer = builder.Configuration["Authentication:Jwt:Issuer"] ?? string.Empty;
var audience = builder.Configuration["Authentication:Jwt:Audience"] ?? string.Empty;
var signingKey = builder.Configuration["Authentication:Jwt:SigningKey"] ?? string.Empty;
if (signingKey.Length < 32)
    throw new InvalidOperationException(
        "Authentication:Jwt:SigningKey must be at least 32 characters. Set it with the environment variable Authentication__Jwt__SigningKey.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep raw "sub" / "unique_name" claim names
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
builder.Services.AddAuthorization();

// ---- Persistence (SQL Server, same NEET_BEE database) -----------------------------
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NeetBee")));

// ---- Payments (gateway chosen by "Payments:Gateway"; only the fake gateway exists today) ---------------
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.SectionName));
var payments = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();
if (!payments.IsFake)
    throw new InvalidOperationException(
        $"Payments:Gateway '{payments.Gateway}' is not implemented. Use 'Fake', or add an IPaymentGateway for it and register it here.");
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddScoped<IPaymentService, PaymentProcessor>();

// ---- Notifications: calls NotificationService over HTTP ("NotificationService" section) ----
builder.Services.AddNeetBeeNotificationClient(builder.Configuration);

// ---- Web / API ----------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "NEET BEE Payment Service", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.Logger.LogWarning("Payments:Gateway is 'Fake' outside Development: payments are recorded as paid but no money is charged.");

// Correlation ID: reuse the ID the gateway forwarded (X-Correlation-Id), or create one.
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
