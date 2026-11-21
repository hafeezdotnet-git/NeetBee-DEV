using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AuthenticationService.CaptchaService;

public sealed class CaptchaValidator
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CaptchaChallenge> _challenges = new();

    public void Store(string id, string answer) => _challenges[id] = new(answer, DateTimeOffset.UtcNow.Add(Lifetime));

    public bool ValidateAndConsume(string? id, string? answer)
    {
        if (string.IsNullOrWhiteSpace(id) || !_challenges.TryRemove(id, out var challenge))
            return false;

        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(answer))
            return false;

        var expected = Encoding.UTF8.GetBytes(challenge.Answer);
        var supplied = Encoding.UTF8.GetBytes(answer.Trim().ToUpperInvariant());
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
