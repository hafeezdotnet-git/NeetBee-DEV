using System.Security.Cryptography;
using System.Text;

namespace AuthenticationService.CaptchaService;

public sealed class CaptchaGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public (string Id, string Answer, string ImageData) Generate()
    {
        var answer = new string(Enumerable.Range(0, 6)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='190' height='58' viewBox='0 0 190 58'><rect width='190' height='58' rx='8' fill='#f1f5f9'/>");
        for (var i = 0; i < answer.Length; i++)
        {
            var rotation = RandomNumberGenerator.GetInt32(-14, 15);
            var y = RandomNumberGenerator.GetInt32(34, 47);
            svg.Append($"<text x='{14 + i * 28}' y='{y}' transform='rotate({rotation} {14 + i * 28} {y})' font-family='Arial,sans-serif' font-size='27' font-weight='700' fill='#17324d'>{answer[i]}</text>");
        }
        svg.Append("</svg>");
        return (id, answer, "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString())));
    }
}
