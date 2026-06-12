using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class BlogCaptchaService(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> redisOptions) : IBlogCaptchaService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly string _prefix = $"{redisOptions.Value.InstanceName}blog_captcha:";

    public async Task<BlogCaptchaDto> CreateAsync(CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(1000, 10000).ToString();
        var id = Guid.NewGuid().ToString("N");
        await _database.StringSetAsync(_prefix + id, code, Lifetime);

        return new BlogCaptchaDto
        {
            CaptchaId = id,
            ImageBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(CreateSvg(code))),
            ExpiresInSeconds = (int)Lifetime.TotalSeconds
        };
    }

    private static string CreateSvg(string code)
    {
        var lines = Enumerable.Range(0, 5)
            .Select(_ =>
            {
                var x1 = RandomNumberGenerator.GetInt32(0, 140);
                var y1 = RandomNumberGenerator.GetInt32(0, 48);
                var x2 = RandomNumberGenerator.GetInt32(0, 140);
                var y2 = RandomNumberGenerator.GetInt32(0, 48);
                var opacity = RandomNumberGenerator.GetInt32(15, 36) / 100.0;
                return $"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='#64748b' stroke-width='1' opacity='{opacity:F2}'/>";
            });

        var chars = code.Select((ch, index) =>
        {
            var x = 18 + index * 27;
            var y = RandomNumberGenerator.GetInt32(30, 39);
            var rotate = RandomNumberGenerator.GetInt32(-18, 19);
            return $"<text x='{x}' y='{y}' transform='rotate({rotate} {x} {y})'>{HtmlEncoder.Default.Encode(ch.ToString())}</text>";
        });

        return $"<svg xmlns='http://www.w3.org/2000/svg' width='140' height='48' viewBox='0 0 140 48'>" +
            "<rect width='140' height='48' rx='8' fill='#f8fafc'/>" +
            string.Concat(lines) +
            "<g font-family='Arial,Helvetica,sans-serif' font-size='28' font-weight='700' fill='#0f172a'>" +
            string.Concat(chars) +
            "</g></svg>";
    }

    public async Task<bool> ValidateAsync(string captchaId, string answer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var key = _prefix + captchaId.Trim();
        var value = await _database.StringGetDeleteAsync(key);
        return value.HasValue
            && string.Equals(value.ToString(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
