using System.Text;
using AuthenticationService.Configuration;
using AuthenticationService.Data;
using AuthenticationService.Entities;
using NeetBee.Common.Logging;
using NeetBee.Common.Notifications;
using AuthenticationService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (console + logs\auth-service-<date>.log, levels from appsettings "Serilog") ----
builder.AddNeetBeeLogging("auth-service");

// ---- Configuration (validated at startup) ----------------------------------
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

var jwt = builder.Configuration.GetSection($"{AuthOptions.SectionName}:Jwt").Get<JwtOptions>() ?? new JwtOptions();

// ---- Persistence (SQL Server, Windows Authentication) -----------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NeetBee")));

// ---- Services ----------------------------------------------------------------
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Sign-in code emails go through NotificationService over HTTP ("NotificationService" section).
builder.Services.AddNeetBeeNotificationClient(builder.Configuration);

// Outbound HTTP client. Used to ask Google's userinfo endpoint who a Gmail sign-in token belongs to.
builder.Services.AddHttpClient(AuthService.GoogleUserInfoClient, client =>
{
    client.BaseAddress = new Uri("https://openidconnect.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ---- Authentication (JWT bearer) ---------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep raw "sub" / "unique_name" claim names
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "unique_name"
        };
    });

builder.Services.AddAuthorization();

// ---- Web / API ----------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "NEET BEE Authentication Service", Version = "v1" });
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
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Correlation ID: reuse the ID the gateway forwarded (X-Correlation-Id), or create one. Every log line written
// while this request runs carries it, so the request can be followed across the web app, gateway and this service.
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

// One summary line per request: method, path, status code and elapsed time.
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Log every unhandled exception with its details, so the cause appears in the service log file.
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
