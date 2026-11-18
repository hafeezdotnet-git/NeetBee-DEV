using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NeetBee.Web.Services;

public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

public sealed record CurrentUser(string Id, string UserName);

public sealed record GatewayResult<T>(bool Ok, T? Value, string? Error);

/// <summary>Calls the API Gateway (and through it, the authentication service) from the server.</summary>
public sealed class GatewayClient(HttpClient http)
{
    private const string Unreachable = "Cannot reach the sign-in service. Please try again shortly.";
    private const string Unexpected = "Unexpected response from the sign-in service.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GatewayResult<TokenResponse>> LoginAsync(string username, string password, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/login", new { username, password }, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                return new GatewayResult<TokenResponse>(false, default, await ReadErrorAsync(response, ct));

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct);
            return tokens is null
                ? new GatewayResult<TokenResponse>(false, default, Unexpected)
                : new GatewayResult<TokenResponse>(true, tokens, null);
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<TokenResponse>(false, default, Unreachable);
        }
    }

    public async Task<GatewayResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/refresh", new { refreshToken }, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                return new GatewayResult<TokenResponse>(false, default, await ReadErrorAsync(response, ct));

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct);
            return tokens is null
                ? new GatewayResult<TokenResponse>(false, default, Unexpected)
                : new GatewayResult<TokenResponse>(true, tokens, null);
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<TokenResponse>(false, default, Unreachable);
        }
    }

    /// <summary>Reads the signed-in identity through the gateway, so the claims come from a validated token.</summary>
    public async Task<GatewayResult<CurrentUser>> MeAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new GatewayResult<CurrentUser>(false, default, Unexpected);

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions, ct);
            if (me is null || string.IsNullOrWhiteSpace(me.Id) || string.IsNullOrWhiteSpace(me.UserName))
                return new GatewayResult<CurrentUser>(false, default, Unexpected);

            return new GatewayResult<CurrentUser>(true, new CurrentUser(me.Id, me.UserName), null);
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<CurrentUser>(false, default, Unreachable);
        }
    }

    public async Task<GatewayResult<bool>> RegisterAsync(string userName, string email, string password, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "api/auth/register", new { userName, email, password }, JsonOptions, ct);

            return response.StatusCode == HttpStatusCode.Created
                ? new GatewayResult<bool>(true, true, null)
                : new GatewayResult<bool>(false, false, await ReadErrorAsync(response, ct));
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<bool>(false, false, Unreachable);
        }
    }

    /// <summary>
    /// Signs in with a Gmail account, creating the account on first use. Sends the Google access token;
    /// the auth service asks Google to verify it. Used by both "Login with Gmail" and "Register with Gmail".
    /// </summary>
    public async Task<GatewayResult<TokenResponse>> GoogleSignInAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/google", new { accessToken }, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                return new GatewayResult<TokenResponse>(false, default, await ReadErrorAsync(response, ct));

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct);
            return tokens is null
                ? new GatewayResult<TokenResponse>(false, default, Unexpected)
                : new GatewayResult<TokenResponse>(true, tokens, null);
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<TokenResponse>(false, default, Unreachable);
        }
    }

    /// <summary>Asks the auth service to email a 6-digit sign-in code.</summary>
    public async Task<GatewayResult<bool>> RequestEmailCodeAsync(string email, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/otp/request", new { email }, JsonOptions, ct);

            return response.StatusCode == HttpStatusCode.Accepted
                ? new GatewayResult<bool>(true, true, null)
                : new GatewayResult<bool>(false, false, await ReadErrorAsync(response, ct));
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<bool>(false, false, Unreachable);
        }
    }

    /// <summary>Checks an emailed code. Signs in, or creates the account on first use.</summary>
    public async Task<GatewayResult<TokenResponse>> VerifyEmailCodeAsync(string email, string code, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/otp/verify", new { email, code }, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                return new GatewayResult<TokenResponse>(false, default, await ReadErrorAsync(response, ct));

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct);
            return tokens is null
                ? new GatewayResult<TokenResponse>(false, default, Unexpected)
                : new GatewayResult<TokenResponse>(true, tokens, null);
        }
        catch (HttpRequestException)
        {
            return new GatewayResult<TokenResponse>(false, default, Unreachable);
        }
    }

    /// <summary>Best effort: sign-out must succeed locally even if the gateway is unreachable.</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/logout", new { refreshToken }, JsonOptions, ct);
        }
        catch (HttpRequestException)
        {
            // The refresh token will expire on its own; the local cookie is cleared regardless.
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, ct);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return problem!.Detail!;
        }
        catch (JsonException)
        {
            // Not a ProblemDetails body; fall back to the status code.
        }
        catch (NotSupportedException)
        {
            // Empty or non-JSON body; fall back to the status code.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Incorrect username or password.",
            HttpStatusCode.Conflict => "An account with this email or username already exists.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Please wait a few minutes and try again.",
            _ => "Something went wrong. Please try again."
        };
    }

    private sealed record ProblemResponse(string? Title, string? Detail);

    private sealed record MeResponse(string? Id, string? UserName);
}
