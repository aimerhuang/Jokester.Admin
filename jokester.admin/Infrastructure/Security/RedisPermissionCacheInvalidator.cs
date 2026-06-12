using jokester.admin.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Infrastructure.Security;

public sealed class RedisPermissionCacheInvalidator(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    ILogger<RedisPermissionCacheInvalidator> logger) : IPermissionCacheInvalidator
{
    private readonly string _prefix = $"{redisOptions.Value.InstanceName}perm:";

    public async Task RemoveUserAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(_prefix + userId);
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable when removing permission cache for user {UserId}.", userId);
        }
    }

    public async Task RemoveAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);
                await foreach (var key in server.KeysAsync(pattern: _prefix + "*"))
                {
                    await redis.GetDatabase().KeyDeleteAsync(key);
                }
            }
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable when clearing permission cache.");
        }
    }
}
