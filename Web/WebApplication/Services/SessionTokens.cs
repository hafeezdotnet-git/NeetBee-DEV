using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace NeetBee.Web.Services;

/// <summary>Builds the token values stored inside the session cookie.</summary>
public static class SessionTokens
{
    public static IEnumerable<AuthenticationToken> From(TokenResponse tokens, DateTimeOffset issuedAt) => new[]
    {
        new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken },
        new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken },
        new AuthenticationToken
        {
            Name = "expires_at",
            Value = issuedAt.AddSeconds(tokens.ExpiresIn).ToString("o", CultureInfo.InvariantCulture)
        }
    };
}
