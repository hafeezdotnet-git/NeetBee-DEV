using System.Reflection;
using System.Text.Json;

namespace NeetBee.Mobile;

/// <summary>
/// Settings read from the embedded appsettings.json at startup.
/// To point the app at another site, change "App:StartUrl" in appsettings.json and rebuild.
/// </summary>
public sealed class AppSettings
{
	const string ResourceName = "NeetBee.Mobile.appsettings.json";

	public required Uri StartUrl { get; init; }

	public static AppSettings Load()
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");

		using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
		{
			CommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
		});

		if (!doc.RootElement.TryGetProperty("App", out var app)
			|| !app.TryGetProperty("StartUrl", out var startUrl)
			|| startUrl.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("appsettings.json must contain \"App\": { \"StartUrl\": \"https://...\" }.");
		}

		var value = startUrl.GetString();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
		{
			throw new InvalidOperationException($"App:StartUrl '{value}' is not a valid http(s) URL.");
		}

		return new AppSettings { StartUrl = uri };
	}
}
