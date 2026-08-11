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
            logger.LogWarning(
                "Redis unavailable when removing permission cache. UserId={UserId}, FailureType={FailureType}",
                userId,
                ex.GetType().Name);
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
            logger.LogWarning(
                "Redis unavailable when clearing permission cache. FailureType={FailureType}",
                ex.GetType().Name);
        }
    }
}
