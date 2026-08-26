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

    private const string RenewScript = """
        if redis.call('ZSCORE', KEYS[1], ARGV[1]) == false then return 0 end
        redis.call('ZADD', KEYS[1], 'XX', ARGV[2], ARGV[1])
        redis.call('EXPIRE', KEYS[1], ARGV[3])
        return 1
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

    public async Task<IAiImageProviderLease> AcquireAsync(CancellationToken cancellationToken)
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
                    return new ProviderLease(
                        database,
                        leasesKey,
                        leaseId,
                        options.ProviderLeaseSeconds,
                        RenewScript,
                        logger);
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

    private sealed class ProviderLease : IAiImageProviderLease
    {
        private readonly IDatabase database;
        private readonly RedisKey leasesKey;
        private readonly RedisValue leaseId;
        private readonly int leaseSeconds;
        private readonly string renewScript;
        private readonly ILogger logger;
        private readonly CancellationTokenSource renewalCancellation = new();
        private readonly CancellationTokenSource leaseLostCancellation = new();
        private readonly Task renewalTask;
        private int disposed;
        private volatile bool isValid = true;

        public ProviderLease(
            IDatabase database,
            RedisKey leasesKey,
            RedisValue leaseId,
            int leaseSeconds,
            string renewScript,
            ILogger logger)
        {
            this.database = database;
            this.leasesKey = leasesKey;
            this.leaseId = leaseId;
            this.leaseSeconds = leaseSeconds;
            this.renewScript = renewScript;
            this.logger = logger;
            renewalTask = RenewUntilDisposedAsync();
        }

        public bool IsValid => isValid && Volatile.Read(ref disposed) == 0;

        public CancellationToken LeaseLostToken => leaseLostCancellation.Token;

        public void ThrowIfLost()
        {
            if (!IsValid)
            {
                throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "AI provider lease was lost.");
            }
        }

        private async Task RenewUntilDisposedAsync()
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, leaseSeconds / 3));
            try
            {
                while (!renewalCancellation.IsCancellationRequested)
                {
                    await Task.Delay(interval, renewalCancellation.Token);
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var renewed = (long)await database.ScriptEvaluateAsync(
                        renewScript,
                        [leasesKey],
                        [leaseId, checked(now + leaseSeconds * 1000L), leaseSeconds + 60]);
                    if (renewed != 1)
                    {
                        MarkLost();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                logger.LogError("AI provider lease renewal failed. FailureType={FailureType}", ex.GetType().Name);
                MarkLost();
            }
        }

        private void MarkLost()
        {
            isValid = false;
            leaseLostCancellation.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            renewalCancellation.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException)
            {
            }
            try
            {
                await database.SortedSetRemoveAsync(leasesKey, leaseId);
            }
            catch (RedisException ex)
            {
                logger.LogWarning("AI provider lease release failed. FailureType={FailureType}", ex.GetType().Name);
            }
            renewalCancellation.Dispose();
            leaseLostCancellation.Dispose();
        }
    }
}
