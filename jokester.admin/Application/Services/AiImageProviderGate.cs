using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class AiImageProviderGate : IAiImageProviderGate
{
    private const string AcquireScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then return 0 end
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[4])
        redis.call('EXPIRE', KEYS[1], ARGV[5])
        return 1
        """;

    private const string FailureScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        if count >= tonumber(ARGV[2]) then
            redis.call('SET', KEYS[2], '1', 'EX', ARGV[3])
        end
        return count
        """;

    private readonly IDatabase database;
    private readonly AiCostControlOptions options;
    private readonly ILogger<AiImageProviderGate> logger;
    private readonly string leasesKey;
    private readonly string failuresKey;
    private readonly string circuitKey;

    public AiImageProviderGate(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> redisOptions,
        IOptions<AiCostControlOptions> options,
        ILogger<AiImageProviderGate> logger)
    {
        database = redis.GetDatabase();
        this.options = options.Value;
        this.logger = logger;
        var prefix = $"{redisOptions.Value.InstanceName}:security:ai:provider";
        leasesKey = $"{prefix}:leases";
        failuresKey = $"{prefix}:failures";
        circuitKey = $"{prefix}:circuit";
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString("N");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                var acquired = (long)await database.ScriptEvaluateAsync(
                    AcquireScript,
                    [leasesKey],
                    [
                        now,
                        checked(now + options.ProviderLeaseSeconds * 1000L),
                        options.MaxGlobalProviderConcurrency,
                        leaseId,
                        options.ProviderLeaseSeconds + 60
                    ]);
                if (acquired == 1)
                {
                    return new ProviderLease(database, leasesKey, leaseId, logger);
                }
            }
            catch (RedisException ex)
            {
                logger.LogError("AI provider concurrency gate failed closed. FailureType={FailureType}", ex.GetType().Name);
                throw new AppException(ErrorCodes.ServiceUnavailable, "AI provider concurrency service is unavailable");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    public async Task ReportSuccessAsync()
    {
        try
        {
            await database.KeyDeleteAsync([failuresKey, circuitKey]);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("AI provider success could not reset the circuit counter. FailureType={FailureType}", ex.GetType().Name);
        }
    }

    public async Task ReportFailureAsync()
    {
        try
        {
            await database.ScriptEvaluateAsync(
                FailureScript,
                [failuresKey, circuitKey],
                [options.ProviderFailureWindowSeconds, options.ProviderFailureThreshold, options.ProviderCircuitOpenSeconds]);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("AI provider failure could not update the circuit counter. FailureType={FailureType}", ex.GetType().Name);
        }
    }

    private sealed class ProviderLease(
        IDatabase database,
        RedisKey leasesKey,
        RedisValue leaseId,
        ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.SortedSetRemoveAsync(leasesKey, leaseId);
            }
            catch (RedisException ex)
            {
                logger.LogWarning("AI provider lease release failed. FailureType={FailureType}", ex.GetType().Name);
            }
        }
    }
}
