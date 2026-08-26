using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class SecurityRateLimitMiddlewareTests
{
    private const long OneMegabyte = 1024 * 1024;

    [Fact]
    public async Task EmailPartition_IsCaseInsensitiveForJsonPropertyNames()
    {
        var counts = new ConcurrentDictionary<string, long>();
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns((string _, RedisKey[] keys, RedisValue[] values, CommandFlags _) =>
            {
                var increment = (long)values[1];
                var count = counts.AddOrUpdate(
                    keys[0].ToString(),
                    increment,
                    (_, current) => current + increment);
                return Task.FromResult(RedisResult.Create((RedisValue)count));
            });

        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var nextCalls = 0;
        var middleware = new SecurityRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);
        var email = $"{Guid.NewGuid():N}@example.test";

        var first = CreateEmailCodeContext("email", email, IPAddress.Parse("192.0.2.1"));
        await middleware.InvokeAsync(first, connection.Object);
        var second = CreateEmailCodeContext("Email", email, IPAddress.Parse("192.0.2.2"));
        await middleware.InvokeAsync(second, connection.Object);

        Assert.Equal(1, nextCalls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
        Assert.True(second.Response.Headers.ContainsKey("Retry-After"));
        second.Response.Body.Position = 0;
        using var response = JsonDocument.Parse(second.Response.Body);
        Assert.Equal("RATE_LIMITED", response.RootElement.GetProperty("code").GetString());
        Assert.True(response.RootElement.GetProperty("details").GetProperty("retryAfterSeconds").GetInt32() > 0);
    }

    [Fact]
    public async Task RedeemRateLimitRejection_IsCapturedByOperationAuditWithoutRequestSecret()
    {
        var counts = new ConcurrentDictionary<string, long>();
        var database = CreateCountingDatabase(counts);
        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        var securityMiddleware = new SecurityRateLimitMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);
        var operationMiddleware = new OperationLogMiddleware(
            context => securityMiddleware.InvokeAsync(context, connection.Object),
            NullLogger<OperationLogMiddleware>.Instance);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(42);
        var auditWriter = new Mock<IAuditLogWriter>();

        DefaultHttpContext? rejected = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var context = CreateRedeemContext(42, IPAddress.Parse("192.0.2.30"));
            await operationMiddleware.InvokeAsync(context, currentUser.Object, auditWriter.Object);
            rejected = context;
        }

        Assert.NotNull(rejected);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        var calls = auditWriter.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IAuditLogWriter.WriteOperationAsync))
            .ToArray();
        Assert.Equal(6, calls.Length);
        var finalCall = calls[^1];
        Assert.Null(finalCall.Arguments[5]);
        var outcomeJson = Assert.IsType<string>(finalCall.Arguments[6]);
        Assert.DoesNotContain("secret-redeem-code", outcomeJson, StringComparison.Ordinal);
        using var outcome = JsonDocument.Parse(outcomeJson);
        Assert.Equal(429, outcome.RootElement.GetProperty("statusCode").GetInt32());
        Assert.False(outcome.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("RATE_LIMITED", outcome.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AiUpload_AllowsTwelveTenMegabyteRequestsWithoutDailyByteLimit()
    {
        var counts = new ConcurrentDictionary<string, long>();
        var windows = new ConcurrentBag<long>();
        var database = CreateCountingDatabase(counts, synchronizeFirstCalls: 12, observedWindows: windows);
        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        var nextCalls = 0;
        var middleware = new SecurityRateLimitMiddleware(
            _ =>
            {
                Interlocked.Increment(ref nextCalls);
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);

        var requests = Enumerable.Range(0, 12)
            .Select(_ => CreateUploadContext("/api/ai/images/upload", 42, 10 * OneMegabyte))
            .ToArray();

        await Task.WhenAll(requests.Select(context => middleware.InvokeAsync(context, connection.Object)));

        Assert.Equal(12, nextCalls);
        Assert.All(requests, context => Assert.NotEqual(
            StatusCodes.Status429TooManyRequests,
            context.Response.StatusCode));
        Assert.Equal(12, windows.Count);
        Assert.All(windows, window => Assert.Equal(60, window));
        Assert.Single(counts);
    }

    [Fact]
    public async Task AiUpload_ThirteenthRequestInMinuteIsRateLimited()
    {
        var counts = new ConcurrentDictionary<string, long>();
        var windows = new ConcurrentBag<long>();
        var database = CreateCountingDatabase(counts, ignoreTimeBuckets: true, observedWindows: windows);
        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        var nextCalls = 0;
        var middleware = new SecurityRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            NullLogger<SecurityRateLimitMiddleware>.Instance);

        DefaultHttpContext? rejected = null;
        for (var attempt = 0; attempt < 13; attempt++)
        {
            rejected = CreateUploadContext("/api/ai/images/upload", 42, OneMegabyte);
            await middleware.InvokeAsync(rejected, connection.Object);
        }

        Assert.NotNull(rejected);
        Assert.Equal(12, nextCalls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.True(rejected.Response.Headers.ContainsKey("Retry-After"));
        var retryAfter = int.Parse(rejected.Response.Headers.RetryAfter.ToString());
        Assert.InRange(retryAfter, 1, 60);
        rejected.Response.Body.Position = 0;
        using var response = JsonDocument.Parse(rejected.Response.Body);
        Assert.Equal("RATE_LIMITED", response.RootElement.GetProperty("code").GetString());
        Assert.Equal(retryAfter, response.RootElement.GetProperty("details").GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(13, windows.Count);
        Assert.All(windows, window => Assert.Equal(60, window));
        Assert.Single(counts);
    }

    private static DefaultHttpContext CreateEmailCodeContext(
        string propertyName,
        string email,
        IPAddress remoteIp)
    {
        var json = Encoding.UTF8.GetBytes($"{{\"{propertyName}\":\"{email}\"}}");
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

    private static DefaultHttpContext CreateRedeemContext(long userId, IPAddress remoteIp)
    {
        var json = Encoding.UTF8.GetBytes("{\"code\":\"secret-redeem-code\"}");
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/points/recharge/redeem";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = json.Length;
        context.Request.Body = new MemoryStream(json);
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = remoteIp;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "test"));
        return context;
    }

    private static DefaultHttpContext CreateUploadContext(string path, long userId, long contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.ContentLength = contentLength;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.40");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "test"));
        return context;
    }

    private static Mock<IDatabase> CreateCountingDatabase(
        ConcurrentDictionary<string, long> counts,
        int synchronizeFirstCalls = 0,
        bool ignoreTimeBuckets = false,
        ConcurrentBag<long>? observedWindows = null)
    {
        var database = new Mock<IDatabase>();
        var synchronizedArrivals = 0;
        var synchronizedCallsReleased = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(async (string _, RedisKey[] keys, RedisValue[] values, CommandFlags _) =>
            {
                if (synchronizeFirstCalls > 0)
                {
                    var arrival = Interlocked.Increment(ref synchronizedArrivals);
                    if (arrival <= synchronizeFirstCalls)
                    {
                        if (arrival == synchronizeFirstCalls)
                        {
                            synchronizedCallsReleased.TrySetResult(true);
                        }
                        await synchronizedCallsReleased.Task;
                    }
                }

                observedWindows?.Add((long)values[0]);
                var increment = (long)values[1];
                var counterKey = ignoreTimeBuckets
                    ? $"window:{values[0]}"
                    : keys[0].ToString();
                var count = counts.AddOrUpdate(
                    counterKey,
                    increment,
                    (_, current) => current + increment);
                return RedisResult.Create((RedisValue)count);
            });
        return database;
    }
}
