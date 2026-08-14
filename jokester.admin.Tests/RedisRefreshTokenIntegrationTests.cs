using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.Security;
using jokester.admin.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace jokester.admin.Tests;

public sealed class RedisRefreshTokenIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RedisScripts_AtomicallyConsumeAndRevokeReplayedFamily_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = true;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var instanceName = $"jokester-security-test:{Guid.NewGuid():N}:";
        var store = new ResilientRefreshTokenStore(
            connection,
            Options.Create(new RedisOptions
            {
                InstanceName = instanceName,
                ConnectionString = connectionString,
                EnableInMemoryRefreshTokenFallback = false
            }),
            Options.Create(new JwtOptions { RefreshTokenExpiresDays = 1 }),
            NullLogger<ResilientRefreshTokenStore>.Instance);

        var expiresAt = DateTime.UtcNow.AddMinutes(1);
        Assert.True(await store.SaveAsync("redis-token", 42, "redis-session", expiresAt, default));

        var results = await Task.WhenAll(
            store.ConsumeAsync("redis-token", default),
            store.ConsumeAsync("redis-token", default));

        Assert.Single(results, x => x.Status == RefreshTokenConsumeStatus.Succeeded);
        Assert.Single(results, x => x.Status == RefreshTokenConsumeStatus.Replayed);
        Assert.False(await store.SaveAsync("redis-rotated-token", 42, "redis-session", expiresAt, default));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RateLimiter_UsesEmailPartitionAcrossDifferentIpAddresses_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = true;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var email = $"{Guid.NewGuid():N}@example.test";
        var firstIp = IPAddress.Parse("198.51.100.10");
        var secondIp = IPAddress.Parse("198.51.100.11");
        var nextCalls = 0;
        var middleware = new SecurityRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);

        try
        {
            var first = CreateEmailCodeContext(email, firstIp);
            await middleware.InvokeAsync(first, connection);
            var second = CreateEmailCodeContext(email, secondIp);
            await middleware.InvokeAsync(second, connection);

            Assert.Equal(1, nextCalls);
            Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
            Assert.True(second.Response.Headers.ContainsKey("Retry-After"));
        }
        finally
        {
            await DeleteRateLimitKeysAsync(connection.GetDatabase(), email, firstIp, secondIp);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RateLimiter_LimitsRedeemCodeIssuanceByUser_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = true;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var userId = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        var remoteIp = IPAddress.Parse("198.51.100.20");
        var nextCalls = 0;
        var middleware = new SecurityRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);

        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var context = CreateRedeemCodeIssueContext(userId, remoteIp);
                await middleware.InvokeAsync(context, connection);
                if (attempt < 5)
                {
                    Assert.NotEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
                }
                else
                {
                    Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
                    Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
                }
            }

            Assert.Equal(5, nextCalls);
        }
        finally
        {
            await DeleteRedeemCodeIssueRateLimitKeysAsync(connection.GetDatabase(), userId, remoteIp);
        }
    }

    private static DefaultHttpContext CreateEmailCodeContext(string email, IPAddress remoteIp)
    {
        var json = Encoding.UTF8.GetBytes($"{{\"email\":\"{email}\"}}");
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/register/email-code";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = json.Length;
        context.Request.Body = new MemoryStream(json);
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = remoteIp;
        return context;
    }

    private static DefaultHttpContext CreateRedeemCodeIssueContext(string userId, IPAddress remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/points/recharge/admin/codes";
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = remoteIp;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "test"));
        return context;
    }

    private static async Task DeleteRateLimitKeysAsync(IDatabase database, string email, params IPAddress[] addresses)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keys = new List<RedisKey>();
        foreach (var address in addresses)
        {
            keys.Add(BuildRateKey("email-code-ip-1m", now / 60, address.ToString()));
            keys.Add(BuildRateKey("email-code-ip-1h", now / 3600, address.ToString()));
            keys.Add(BuildRateKey("email-code-ip-1d", now / 86400, address.ToString()));
        }
        keys.Add(BuildRateKey("email-code-email-1m", now / 60, email));
        keys.Add(BuildRateKey("email-code-email-1h", now / 3600, email));
        keys.Add(BuildRateKey("email-code-email-1d", now / 86400, email));
        await database.KeyDeleteAsync(keys.ToArray());
    }

    private static Task DeleteRedeemCodeIssueRateLimitKeysAsync(
        IDatabase database,
        string userId,
        IPAddress remoteIp)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        RedisKey[] keys =
        [
            BuildRateKey("point-code-issue-user-1h", now / 3600, userId),
            BuildRateKey("point-code-issue-user-1d", now / 86400, userId),
            BuildRateKey("point-code-issue-ip-1h", now / 3600, remoteIp.ToString()),
            BuildRateKey("point-code-issue-ip-1d", now / 86400, remoteIp.ToString())
        ];
        return database.KeyDeleteAsync(keys);
    }

    private static RedisKey BuildRateKey(string name, long bucket, string identity)
    {
        var material = $"{name}:{bucket}:{identity}";
        return "jokester:security-rate:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
