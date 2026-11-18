using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace NeetBee.Common.Logging;

/// <summary>
/// Logging for the NEET BEE services.
/// - Levels come from the "Serilog:MinimumLevel" section of appsettings.json (Verbose, Debug, Information, Warning, Error, Fatal).
/// - Output goes to the console and to a daily file: logs\&lt;prefix&gt;-yyyyMMdd.log (folder set by "LogFiles:Directory").
/// - Each line carries the CorrelationId of the request it belongs to.
/// </summary>
public static class LoggingSetup
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <param name="logFilePrefix">Start of the log file name, e.g. "auth-service".</param>
    public static WebApplicationBuilder AddNeetBeeLogging(this WebApplicationBuilder builder, string logFilePrefix)
    {
        var fileSettings = builder.Configuration.GetSection("LogFiles");

        // Relative folders are resolved against the project folder, so the log file is easy to find.
        var logDirectory = Path.GetFullPath(
            Path.Combine(builder.Environment.ContentRootPath, fileSettings["Directory"] ?? "logs"));
        var retainedFiles = fileSettings.GetValue("RetainedFileCount", 14);

        builder.Host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, $"{logFilePrefix}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFiles,
                outputTemplate: OutputTemplate));

        return builder;
    }
}
