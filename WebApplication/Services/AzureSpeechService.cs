using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using NeetBee.Web.Models;

namespace NeetBee.Web.Services;

/// <summary>
/// Text to speech through the Azure AI Speech REST API (no SDK package needed).
/// Configure "Speech:Azure:Key" and "Speech:Azure:Region" with user secrets or environment variables.
/// The free F0 tier covers 0.5 million neural characters a month, and repeated phrases are served from memory.
/// When the key is missing, the Voice Q&amp;A page uses the browser's built-in speech instead.
/// </summary>
public sealed partial class AzureSpeechService(
    HttpClient http,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<AzureSpeechService> logger)
{
    public const int MaxTextLength = 1500;

    /// <summary>
    /// Five en-IN (English, India) neural voices listed in Microsoft's Azure AI Speech voice catalogue.
    /// </summary>
    public static readonly IReadOnlyList<SpeechVoice> Voices =
    [
        new("en-IN-NeerjaNeural", "Neerja (Female)", "Female", "Neerja"),
        new("en-IN-PrabhatNeural", "Prabhat (Male)", "Male", "Prabhat"),
        new("en-IN-AashiNeural", "Aashi (Female)", "Female", "Aashi"),
        new("en-IN-AaravNeural", "Aarav (Male)", "Male", "Aarav"),
        new("en-IN-KavyaNeural", "Kavya (Female)", "Female", "Kavya"),
    ];

    /// <summary>Speeds offered on the page. Only these values are accepted.</summary>
    public static readonly IReadOnlyList<double> Rates = [0.75, 1.0, 1.2, 1.5];

    private string? Key => configuration["Speech:Azure:Key"];
    private string? Region => configuration["Speech:Azure:Region"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Region) && RegionPattern().IsMatch(Region!);

    public static bool IsKnownVoice(string? voice) => Voices.Any(v => v.AzureName == voice);

    public static bool IsKnownRate(double rate) => Rates.Any(r => Math.Abs(r - rate) < 0.001);

    /// <summary>Returns MP3 audio for the text, or null when Azure is not configured or the call fails.</summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string voice, double rate, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;

        var cacheKey = "tts:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{voice}|{rate}|{text}")));
        if (cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null) return cached;

        var percent = (int)Math.Round((rate - 1.0) * 100);
        var rateText = (percent >= 0 ? "+" : "") + percent.ToString(CultureInfo.InvariantCulture) + "%";
        var ssml =
            "<speak version='1.0' xml:lang='en-IN'>" +
            $"<voice name='{voice}'><prosody rate='{rateText}'>{SecurityElement.Escape(text)}</prosody></voice>" +
            "</speak>";

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1");
        request.Headers.Add("Ocp-Apim-Subscription-Key", Key);
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        request.Headers.UserAgent.ParseAdd("NeetBeeWeb/1.0");
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Azure speech returned {StatusCode} for voice {Voice}", (int)response.StatusCode, voice);
                return null;
            }

            var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            cache.Set(cacheKey, audio, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(6) });
            return audio;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Azure speech request failed for voice {Voice}", voice);
            return null;
        }
    }

    [GeneratedRegex("^[a-z0-9]{3,30}$")]
    private static partial Regex RegionPattern();
}
