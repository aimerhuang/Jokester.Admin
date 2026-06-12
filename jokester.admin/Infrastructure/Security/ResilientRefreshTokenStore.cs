using System.Collections.Concurrent;
using jokester.admin.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Infrastructure.Security;

public sealed class ResilientRefreshTokenStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> redisOptions,
    ILogger<ResilientRefreshTokenStore> logger) : IRefreshTokenStore
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly string _prefix = $"{redisOptions.Value.InstanceName}refresh_token:";
    private readonly bool _enableFallback = redisOptions.Value.EnableInMemoryRefreshTokenFallback;
    private readonly ConcurrentDictionary<string, RefreshTokenEntry> _fallbackTokens = new();

    public async Task SaveAsync(string refreshToken, long userId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromSeconds(1);
        }

        try
        {
            await _database.StringSetAsync(_prefix + refreshToken, userId, ttl);
            _fallbackTokens.TryRemove(refreshToken, out _);
        }
        catch (RedisConnectionException ex) when (_enableFallback)
        {
            logger.LogWarning(ex, "Redis unavailable when saving refresh token. Falling back to in-memory store.");
            _fallbackTokens[refreshToken] = new RefreshTokenEntry(userId, expiresAt);
        }
    }

    public async Task<long?> GetUserIdAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var value = await _database.StringGetAsync(_prefix + refreshToken);
            if (value.HasValue && long.TryParse(value.ToString(), out var userId))
            {
                return userId;
            }
        }
        catch (RedisConnectionException ex) when (_enableFallback)
        {
            logger.LogWarning(ex, "Redis unavailable when reading refresh token. Falling back to in-memory store.");
        }

        if (!_enableFallback)
        {
            return null;
        }

        return TryGetFallbackUserId(refreshToken);
    }

    public async Task RemoveAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            await _database.KeyDeleteAsync(_prefix + refreshToken);
        }
        catch (RedisConnectionException ex) when (_enableFallback)
        {
            logger.LogWarning(ex, "Redis unavailable when removing refresh token. Cleaning up in-memory fallback only.");
        }

        _fallbackTokens.TryRemove(refreshToken, out _);
    }

    private long? TryGetFallbackUserId(string refreshToken)
    {
        if (!_fallbackTokens.TryGetValue(refreshToken, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            _fallbackTokens.TryRemove(refreshToken, out _);
            return null;
        }

        return entry.UserId;
    }

    private sealed record RefreshTokenEntry(long UserId, DateTime ExpiresAt);
}
