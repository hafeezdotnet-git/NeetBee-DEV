using Microsoft.Extensions.Options;

namespace AuthenticationService.Configuration;

/// <summary>Fails application startup when the authentication configuration is incomplete.</summary>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var errors = new List<string>();

        if (!options.IsTokenBased && !options.IsExisting)
            errors.Add("Authentication:Auth_type must be 'TokenBased' or 'Existing'.");

        if (options.IsTokenBased)
        {
            if (string.IsNullOrWhiteSpace(options.Jwt.Issuer))
                errors.Add("Authentication:Jwt:Issuer is required.");

            if (string.IsNullOrWhiteSpace(options.Jwt.Audience))
                errors.Add("Authentication:Jwt:Audience is required.");

            if (string.IsNullOrWhiteSpace(options.Jwt.SigningKey) || options.Jwt.SigningKey.Length < 32)
                errors.Add("Authentication:Jwt:SigningKey must be at least 32 characters. Set it with the environment variable Authentication__Jwt__SigningKey.");

            if (options.Jwt.AccessTokenExpiryMinutes is < 1 or > 60)
                errors.Add("Authentication:Jwt:AccessTokenExpiryMinutes must be between 1 and 60.");

            if (options.Jwt.RefreshTokenExpiryDays is < 1 or > 90)
                errors.Add("Authentication:Jwt:RefreshTokenExpiryDays must be between 1 and 90.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
