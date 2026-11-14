using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NeetBee.Web;
using NeetBee.Web.Controllers;
using NeetBee.Web.Logging;
using NeetBee.Web.Services;
using Serilog;
using Serilog.Context;

// Fully qualified so the call does not depend on any local type or namespace name.
var builder = global::Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// ---- Logging (console + logs\web-<date>.log, levels from appsettings "Serilog") ----
builder.AddNeetBeeLogging("web");

// Site root holding the static NEET BEE pages (index.html, css, js, assets, login).
// Default: this project folder (backend\WebApplication).
var configuredRoot = builder.Configuration["Site:RootPath"] ?? ".";
var siteRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredRoot));

if (!Directory.Exists(siteRoot))
    throw new InvalidOperationException($"Site:RootPath does not exist: {siteRoot}");

var siteFiles = new SiteFileProvider(siteRoot);

var gatewayUrl = builder.Configuration["Gateway:BaseUrl"]
    ?? throw new InvalidOperationException("Gateway:BaseUrl is not configured.");
var secureCookies = builder.Configuration.GetValue<bool>("Session:SecureCookies");

// Gmail (Google) sign-in. The client ID and secret come from user secrets or environment variables,
// never from source control. Without them the Gmail buttons show a message instead of starting Google sign-in.
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

builder.Services.AddControllersWithViews();

// Lets the gateway handler below read the current request's correlation ID.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationIdHandler>();

// Learning progress for the dashboard. Returns empty progress until a progress store is connected.
builder.Services.AddScoped<IStudentProgressService, StudentProgressService>();

// Voice Q&A: questions from SQL Server (dbo.Questions), and Azure AI Speech (optional; the page falls back to browser speech without a key).
builder.Services.AddScoped<IVoiceQuestionService, SqlVoiceQuestionService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<AzureSpeechService>(client => client.Timeout = TimeSpan.FromSeconds(15));

// Server-to-server calls to the gateway. The browser never talks to the gateway directly.
// CorrelationIdHandler passes this request's correlation ID on, so the gateway and service logs match it.
builder.Services.AddHttpClient<GatewayClient>(client =>
{
    client.BaseAddress = new Uri(gatewayUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
})
.AddHttpMessageHandler<CorrelationIdHandler>();

// The access and refresh tokens live inside this encrypted, HttpOnly cookie. JavaScript never sees them.
var authBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "neetbee.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = RefreshSessionIfNeededAsync;
    })
    // Short-lived cookie that holds the Google sign-in result until the Gmail callback finishes.
    .AddCookie(AccountController.ExternalScheme);

if (googleEnabled)
{
    // Google sends the user back to /signin-google. SaveTokens keeps the Google access token,
    // which the auth service uses to ask Google who the user is.
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SignInScheme = AccountController.ExternalScheme;
        options.SaveTokens = true;
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// Correlation ID: one ID per web request. Reuses the caller's X-Correlation-Id if sane, otherwise creates one.
// Every log line written while the request runs carries it.
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

app.UseExceptionHandler("/error");
app.UseStatusCodePages();

// Public static site. Each site folder is mapped directly so its URLs match the static site
// (/css/style.css, /js/main.js, /assets/..., /login/index.html).
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = siteFiles });

foreach (var folder in new[] { "css", "js", "assets", "login" })
{
    var folderPath = Path.Combine(siteRoot, folder);
    if (Directory.Exists(folderPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folderPath),
            RequestPath = $"/{folder}"
        });
    }
}

// Top-level site HTML pages (index.html, about.html, ...), see SiteFileProvider.
app.UseStaticFiles(new StaticFileOptions { FileProvider = siteFiles });

// Web app assets (wwwroot\css\app.css). Requests not found above fall through to here.
app.UseStaticFiles();

// One summary line per page request: method, path, status code and elapsed time.
app.UseSerilogRequestLogging();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", site = "NEET BEE" }));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

// Runs on every request that carries the session cookie. Refreshes the access token shortly before it
// expires, using the refresh token. If refresh fails, the user is signed out.
static async Task RefreshSessionIfNeededAsync(CookieValidatePrincipalContext context)
{
    var properties = context.Properties;

    var expiresText = properties.GetTokenValue("expires_at");
    if (DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
        && expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
    {
        return;
    }

    var refreshToken = properties.GetTokenValue("refresh_token");
    if (string.IsNullOrEmpty(refreshToken))
    {
        context.RejectPrincipal();
        return;
    }

    var gateway = context.HttpContext.RequestServices.GetRequiredService<GatewayClient>();
    var refreshed = await gateway.RefreshAsync(refreshToken, context.HttpContext.RequestAborted);

    if (!refreshed.Ok || refreshed.Value is null)
    {
        context.RejectPrincipal();
        return;
    }

    properties.StoreTokens(SessionTokens.From(refreshed.Value, DateTimeOffset.UtcNow));
    context.ShouldRenew = true;
}
