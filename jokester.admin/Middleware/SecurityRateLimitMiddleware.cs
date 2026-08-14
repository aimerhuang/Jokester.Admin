using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Common;
using StackExchange.Redis;

namespace jokester.admin.Middleware;

public sealed class SecurityRateLimitMiddleware(RequestDelegate next, ILogger<SecurityRateLimitMiddleware> logger)
{
    private const long MaxUploadBytesPerDay = 50L * 1024 * 1024;
    private const long UnknownUploadChargeBytes = 10L * 1024 * 1024;
    private const int MaxPartitionFieldLength = 256;
    private const string FixedWindowScript = """
        local current = redis.call('INCRBY', KEYS[1], ARGV[2])
        if current == tonumber(ARGV[2]) then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return current
        """;

    public async Task InvokeAsync(HttpContext context, IConnectionMultiplexer redis)
    {
        var rules = GetRules(context);
        if (rules.Count == 0)
        {
            await next(context);
            return;
        }

        Dictionary<string, string?>? bodyFields = null;
        try
        {
            foreach (var rule in rules)
            {
                string identity;
                if (rule.Partition == RateLimitPartition.BodyField)
                {
                    bodyFields ??= await ReadBodyFieldsAsync(context.Request, context.RequestAborted);
                    identity = NormalizePartitionValue(bodyFields.GetValueOrDefault(rule.BodyField!))
                        ?? "missing:" + ResolveRemoteIp(context);
                }
                else
                {
                    identity = ResolveIdentity(context, rule.Partition);
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var bucket = now / rule.WindowSeconds;
                var keyMaterial = $"{rule.Name}:{bucket}:{identity}";
                var key = "jokester:security-rate:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
                var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                    FixedWindowScript,
                    [key],
                    [rule.WindowSeconds, rule.Weight]);
                if (count > rule.Limit)
                {
                    await WriteRejectedAsync(context, rule.WindowSeconds, now);
                    return;
                }
            }
        }
        catch (RedisException ex)
        {
            logger.LogError(
                "Shared rate limiter is unavailable; request rejected. Route={Route}, FailureType={FailureType}",
                context.Request.Path,
                ex.GetType().Name);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                ApiErrorResponse.Failure(
                    MachineErrorCodes.ServiceUnavailable,
                    "Rate limit service unavailable.",
                    context.TraceIdentifier),
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static IReadOnlyList<RateLimitRule> GetRules(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        var uploadWeight = Math.Clamp(context.Request.ContentLength ?? UnknownUploadChargeBytes, 1, MaxUploadBytesPerDay);

        if (HttpMethods.IsPost(method) && path.Equals("/api/auth/login"))
        {
            return
            [
                new("login-ip-10m", RateLimitPartition.Ip, 10, 600),
                new("login-account-10m", RateLimitPartition.BodyField, 10, 600, "userName"),
                new("login-device-10m", RateLimitPartition.BodyField, 10, 600, "deviceSessionId")
            ];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/auth/refresh"))
        {
            return [new("refresh-ip-10m", RateLimitPartition.Ip, 30, 600)];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/auth/register/email-code"))
        {
            return
            [
                new("email-code-ip-1m", RateLimitPartition.Ip, 1, 60),
                new("email-code-ip-1h", RateLimitPartition.Ip, 5, 3600),
                new("email-code-ip-1d", RateLimitPartition.Ip, 10, 86400),
                new("email-code-email-1m", RateLimitPartition.BodyField, 1, 60, "email"),
                new("email-code-email-1h", RateLimitPartition.BodyField, 5, 3600, "email"),
                new("email-code-email-1d", RateLimitPartition.BodyField, 10, 86400, "email")
            ];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/auth/register"))
        {
            return
            [
                new("register-ip-1h", RateLimitPartition.Ip, 5, 3600),
                new("register-email-1h", RateLimitPartition.BodyField, 5, 3600, "email")
            ];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/points/recharge/redeem"))
        {
            return
            [
                new("point-redeem-user-1m", RateLimitPartition.User, 5, 60),
                new("point-redeem-user-1d", RateLimitPartition.User, 20, 86400),
                new("point-redeem-ip-1m", RateLimitPartition.Ip, 10, 60)
            ];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/points/recharge/admin/codes"))
        {
            return
            [
                new("point-code-issue-user-1h", RateLimitPartition.User, 5, 3600),
                new("point-code-issue-user-1d", RateLimitPartition.User, 20, 86400),
                new("point-code-issue-ip-1h", RateLimitPartition.Ip, 10, 3600),
                new("point-code-issue-ip-1d", RateLimitPartition.Ip, 40, 86400)
            ];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/points/recharge/orders"))
        {
            return [new("point-order-user-1h", RateLimitPartition.User, 20, 3600)];
        }
        if (HttpMethods.IsPost(method)
            && path.StartsWithSegments("/api/prompts", out var promptPath)
            && promptPath.Value?.EndsWith("/events", StringComparison.OrdinalIgnoreCase) == true)
        {
            return [new("prompt-event-ip-1m", RateLimitPartition.Ip, 60, 60)];
        }
        if (HttpMethods.IsGet(method) && path.Equals("/api/blog/comments/captcha"))
        {
            return [new("comment-captcha-ip-1m", RateLimitPartition.Ip, 10, 60)];
        }
        if (HttpMethods.IsPost(method) && path.Equals("/api/blog/comments/public"))
        {
            return
            [
                new("comment-ip-1m", RateLimitPartition.Ip, 3, 60),
                new("comment-ip-1d", RateLimitPartition.Ip, 20, 86400)
            ];
        }
        if (HttpMethods.IsPost(method)
            && (path.Equals("/api/ai/images/upload") || path.Equals("/api/blog/media/upload")))
        {
            return
            [
                new("upload-user-1m", RateLimitPartition.User, 5, 60),
                new("upload-user-bytes-1d", RateLimitPartition.User, MaxUploadBytesPerDay, 86400, Weight: uploadWeight)
            ];
        }
        if (HttpMethods.IsPost(method)
            && path.StartsWithSegments("/api/ai/images")
            && !path.Equals("/api/ai/images/parameters/resolve"))
        {
            return [new("ai-create-user-1m", RateLimitPartition.User, 2, 60)];
        }
        if (HttpMethods.IsGet(method) && path.StartsWithSegments("/api"))
        {
            return [new("api-read-ip-1m", RateLimitPartition.Ip, 120, 60)];
        }

        return [];
    }

    private static string ResolveIdentity(HttpContext context, RateLimitPartition partition)
    {
        return partition switch
        {
            RateLimitPartition.User when context.User.Identity?.IsAuthenticated == true =>
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "authenticated-unknown",
            RateLimitPartition.User => "anonymous:" + ResolveRemoteIp(context),
            _ => ResolveRemoteIp(context)
        };
    }

    private static string ResolveRemoteIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async Task<Dictionary<string, string?>> ReadBodyFieldsAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (request.ContentLength is null or <= 0 or > 64 * 1024
            || request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return result;
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("userName", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("email", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("deviceSessionId", StringComparison.OrdinalIgnoreCase))
                {
                    result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return result;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static string? NormalizePartitionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= MaxPartitionFieldLength
            ? normalized
            : normalized[..MaxPartitionFieldLength];
    }

    private static async Task WriteRejectedAsync(HttpContext context, int windowSeconds, long now)
    {
        var retryAfter = Math.Max(1, windowSeconds - (int)(now % windowSeconds));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter.ToString();
        await context.Response.WriteAsJsonAsync(
            ApiErrorResponse.Failure(
                MachineErrorCodes.RateLimited,
                "Too many requests.",
                context.TraceIdentifier,
                new { retryAfterSeconds = retryAfter }),
            context.RequestAborted);
    }

    private enum RateLimitPartition
    {
        Ip,
        User,
        BodyField
    }

    private readonly record struct RateLimitRule(
        string Name,
        RateLimitPartition Partition,
        long Limit,
        int WindowSeconds,
        string? BodyField = null,
        long Weight = 1);
}
