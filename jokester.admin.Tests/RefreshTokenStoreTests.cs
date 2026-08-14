using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class RefreshTokenStoreTests
{
    [Fact]
    public async Task ConcurrentConsume_ReturnsCompletedRotation_ThenRevokesFamilyAfterGrace()
    {
        var store = CreateFallbackStore();
        var token = "refresh-token-1";
        var sessionId = "session-1";
        Assert.True(await store.SaveAsync(token, 42, sessionId, DateTime.UtcNow.AddDays(1), default));

        var first = await store.ConsumeAsync(token, default);
        Assert.Equal(RefreshTokenConsumeStatus.Succeeded, first.Status);
        var expected = new RefreshTokenRotationTokens(
            "access-2",
            "refresh-2",
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow.AddDays(1));
        Assert.True(await store.CompleteRotationAsync(token, expected.RefreshToken, expected, default));

        var concurrent = await store.ConsumeAsync(token, default);
        Assert.Equal(RefreshTokenConsumeStatus.Concurrent, concurrent.Status);
        Assert.Equal(expected, concurrent.Tokens);
    }

    [Fact]
    public async Task RevokeUserSessions_PreventsRotationWithinExistingFamily()
    {
        var store = CreateFallbackStore();
        Assert.True(await store.SaveAsync("refresh-token-2", 42, "session-2", DateTime.UtcNow.AddDays(1), default));

        await store.RevokeUserSessionsAsync(42, default);

        var result = await store.ConsumeAsync("refresh-token-2", default);
        Assert.Equal(RefreshTokenConsumeStatus.Invalid, result.Status);
        Assert.False(await store.SaveAsync("rotated-token-2", 42, "session-2", DateTime.UtcNow.AddDays(1), default));
    }

    private static ResilientRefreshTokenStore CreateFallbackStore()
    {
        var redisFailure = new RedisConnectionException(
            ConnectionFailureType.UnableToResolvePhysicalConnection,
            "Redis intentionally unavailable in fallback-state tests.");
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(redisFailure);

        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return new ResilientRefreshTokenStore(
            connection.Object,
            Options.Create(new RedisOptions
            {
                InstanceName = "test:",
                ConnectionString = "unused",
                EnableInMemoryRefreshTokenFallback = true
            }),
            Options.Create(new JwtOptions { RefreshTokenExpiresDays = 7 }),
            NullLogger<ResilientRefreshTokenStore>.Instance);
    }
}
