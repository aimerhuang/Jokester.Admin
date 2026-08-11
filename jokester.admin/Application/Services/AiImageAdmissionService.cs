using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class AiImageAdmissionService : IAiImageAdmissionService
{
    private const string ReserveScript = """
        local existing = redis.call('GET', KEYS[1])
        if existing then
            if string.sub(existing, 1, 64) ~= ARGV[1] then return {'conflict', '0'} end
            return {'duplicate', string.sub(existing, 66)}
        end
        if redis.call('EXISTS', KEYS[6]) == 1 then return {'circuit', '0'} end
        local active = tonumber(redis.call('GET', KEYS[2]) or '0')
        local dailyImages = tonumber(redis.call('GET', KEYS[3]) or '0')
        local dailyPoints = tonumber(redis.call('GET', KEYS[4]) or '0')
        local globalActive = tonumber(redis.call('GET', KEYS[5]) or '0')
        if active + tonumber(ARGV[2]) > tonumber(ARGV[4]) then return {'concurrency', '0'} end
        if dailyImages + tonumber(ARGV[2]) > tonumber(ARGV[5]) then return {'image_quota', '0'} end
        if dailyPoints + tonumber(ARGV[3]) > tonumber(ARGV[6]) then return {'point_quota', '0'} end
        if globalActive + tonumber(ARGV[2]) > tonumber(ARGV[10]) then return {'queue', '0'} end
        local claimed = redis.call('SET', KEYS[1], ARGV[1] .. '|0', 'NX', 'EX', ARGV[7])
        if not claimed then
            existing = redis.call('GET', KEYS[1])
            if existing and string.sub(existing, 1, 64) == ARGV[1] then
                return {'duplicate', string.sub(existing, 66)}
            end
            return {'conflict', '0'}
        end
        redis.call('INCRBY', KEYS[2], ARGV[2])
        redis.call('EXPIRE', KEYS[2], ARGV[8])
        redis.call('INCRBY', KEYS[5], ARGV[2])
        redis.call('EXPIRE', KEYS[5], ARGV[8])
        redis.call('INCRBY', KEYS[3], ARGV[2])
        redis.call('EXPIRE', KEYS[3], ARGV[9])
        redis.call('INCRBY', KEYS[4], ARGV[3])
        redis.call('EXPIRE', KEYS[4], ARGV[9])
        return {'reserved', '0'}
        """;

    private const string BindScript = """
        local expected = ARGV[1] .. '|0'
        if redis.call('GET', KEYS[1]) ~= expected then return 0 end
        local ttl = redis.call('TTL', KEYS[1])
        redis.call('SET', KEYS[1], ARGV[1] .. '|' .. ARGV[2])
        if ttl > 0 then redis.call('EXPIRE', KEYS[1], ttl) end
        return 1
        """;

    private const string CancelScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] .. '|0' then return 0 end
        redis.call('DEL', KEYS[1])
        local active = tonumber(redis.call('GET', KEYS[2]) or '0')
        redis.call('SET', KEYS[2], math.max(0, active - tonumber(ARGV[2])), 'KEEPTTL')
        local globalActive = tonumber(redis.call('GET', KEYS[3]) or '0')
        redis.call('SET', KEYS[3], math.max(0, globalActive - tonumber(ARGV[2])), 'KEEPTTL')
        local images = tonumber(redis.call('GET', KEYS[4]) or '0')
        redis.call('SET', KEYS[4], math.max(0, images - tonumber(ARGV[2])), 'KEEPTTL')
        local points = tonumber(redis.call('GET', KEYS[5]) or '0')
        redis.call('SET', KEYS[5], math.max(0, points - tonumber(ARGV[3])), 'KEEPTTL')
        return 1
        """;

    private const string CompleteScript = """
        local recorded = redis.call('SET', KEYS[1], '1', 'NX', 'EX', ARGV[4])
        if not recorded then return 0 end
        local active = tonumber(redis.call('GET', KEYS[2]) or '0')
        redis.call('SET', KEYS[2], math.max(0, active - tonumber(ARGV[1])), 'KEEPTTL')
        local globalActive = tonumber(redis.call('GET', KEYS[3]) or '0')
        redis.call('SET', KEYS[3], math.max(0, globalActive - tonumber(ARGV[1])), 'KEEPTTL')
        local images = tonumber(redis.call('GET', KEYS[4]) or '0')
        redis.call('SET', KEYS[4], math.max(0, images - tonumber(ARGV[2])), 'KEEPTTL')
        local points = tonumber(redis.call('GET', KEYS[5]) or '0')
        redis.call('SET', KEYS[5], math.max(0, points - tonumber(ARGV[3])), 'KEEPTTL')
        return 1
        """;

    private readonly IDatabase database;
    private readonly AiCostControlOptions options;
    private readonly IAiImageTaskQueue taskQueue;
    private readonly ILogger<AiImageAdmissionService> logger;
    private readonly string keyPrefix;

    public AiImageAdmissionService(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> redisOptions,
        IOptions<AiCostControlOptions> options,
        IAiImageTaskQueue taskQueue,
        ILogger<AiImageAdmissionService> logger)
    {
        database = redis.GetDatabase();
        this.options = options.Value;
        this.taskQueue = taskQueue;
        this.logger = logger;
        keyPrefix = $"{redisOptions.Value.InstanceName}:security:ai";
    }

    public async Task<AiImageAdmissionReservation> ReserveAsync(
        long userId,
        string idempotencyKeyHash,
        string requestFingerprint,
        int imageCount,
        int pointCost,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (taskQueue.BacklogCount + imageCount > Math.Min(options.MaxQueuedTasks, taskQueue.Capacity))
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, "AI image task queue is at capacity");
        }

        var quotaDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMdd");
        var reservation = new AiImageAdmissionReservation(
            userId,
            idempotencyKeyHash,
            requestFingerprint,
            quotaDate,
            imageCount,
            pointCost,
            false,
            0);

        try
        {
            var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
                ReserveScript,
                [
                    IdempotencyKey(userId, idempotencyKeyHash),
                    ActiveKey(userId),
                    DailyImagesKey(userId, quotaDate),
                    DailyPointsKey(userId, quotaDate),
                    GlobalActiveKey,
                    CircuitKey
                ],
                [
                    requestFingerprint,
                    imageCount,
                    pointCost,
                    options.MaxConcurrentTasksPerUser,
                    options.DailyImageLimitPerUser,
                    options.DailyPointLimitPerUser,
                    checked(options.IdempotencyTtlHours * 3600),
                    checked(options.ReservationTtlMinutes * 60),
                    ResolveDailyTtlSeconds(),
                    options.MaxQueuedTasks
                ]);
            cancellationToken.ThrowIfCancellationRequested();

            var status = result is { Length: >= 2 } ? result[0].ToString() : null;
            return status switch
            {
                "reserved" => reservation,
                "duplicate" => reservation with
                {
                    IsDuplicate = true,
                    ExistingTaskId = long.TryParse(result![1].ToString(), out var taskId) ? taskId : 0
                },
                "conflict" => throw new ConflictException("Idempotency key was already used with a different request"),
                "concurrency" => throw new AppException(ErrorCodes.TooManyRequests, "An AI image task is already active for this user"),
                "image_quota" => throw new AppException(ErrorCodes.TooManyRequests, "Daily AI image quota exceeded"),
                "point_quota" => throw new AppException(ErrorCodes.TooManyRequests, "Daily AI point quota exceeded"),
                "queue" => throw new AppException(ErrorCodes.ServiceUnavailable, "Global AI image task backlog is at capacity"),
                "circuit" => throw new AppException(ErrorCodes.ServiceUnavailable, "AI image provider circuit is open"),
                _ => throw new AppException(ErrorCodes.ServiceUnavailable, "AI cost control service returned an invalid response")
            };
        }
        catch (RedisException ex)
        {
            logger.LogError("AI cost admission failed closed. FailureType={FailureType}", ex.GetType().Name);
            throw new AppException(ErrorCodes.ServiceUnavailable, "AI cost control service is unavailable");
        }
    }

    public async Task BindTaskAsync(AiImageAdmissionReservation reservation, long taskId, CancellationToken cancellationToken)
    {
        if (reservation.IsDuplicate)
        {
            return;
        }

        try
        {
            var bound = (long)await database.ScriptEvaluateAsync(
                BindScript,
                [IdempotencyKey(reservation.UserId, reservation.IdempotencyKeyHash)],
                [reservation.RequestFingerprint, taskId]);
            cancellationToken.ThrowIfCancellationRequested();
            if (bound != 1)
            {
                throw new AppException(ErrorCodes.ServiceUnavailable, "AI task idempotency reservation expired before it was committed");
            }
        }
        catch (RedisException ex)
        {
            logger.LogError("AI idempotency binding failed. TaskId={TaskId}, FailureType={FailureType}", taskId, ex.GetType().Name);
            throw new AppException(ErrorCodes.ServiceUnavailable, "AI cost control service is unavailable");
        }
    }

    public async Task CancelAsync(AiImageAdmissionReservation reservation)
    {
        if (reservation.IsDuplicate)
        {
            return;
        }

        try
        {
            await database.ScriptEvaluateAsync(
                CancelScript,
                [
                    IdempotencyKey(reservation.UserId, reservation.IdempotencyKeyHash),
                    ActiveKey(reservation.UserId),
                    GlobalActiveKey,
                    DailyImagesKey(reservation.UserId, reservation.QuotaDate),
                    DailyPointsKey(reservation.UserId, reservation.QuotaDate)
                ],
                [reservation.RequestFingerprint, reservation.ImageCount, reservation.PointCost]);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("AI admission compensation failed. UserId={UserId}, FailureType={FailureType}", reservation.UserId, ex.GetType().Name);
        }
    }

    public async Task CompleteAsync(AiImageTaskEntity task, int completedImageCount, int refundedPoints)
    {
        var failedImageCount = Math.Max(0, task.ImageCount - completedImageCount);
        // ai_image_task.created_at 保存的是香港本地墙上时间，不依赖 DateTime.Kind 做二次时区换算。
        var quotaDate = task.CreatedAt.ToString("yyyyMMdd");
        try
        {
            await database.ScriptEvaluateAsync(
                CompleteScript,
                [
                    $"{keyPrefix}:settled:{task.Id}",
                    ActiveKey(task.UserId),
                    GlobalActiveKey,
                    DailyImagesKey(task.UserId, quotaDate),
                    DailyPointsKey(task.UserId, quotaDate)
                ],
                [task.ImageCount, failedImageCount, Math.Max(0, refundedPoints), 7 * 24 * 3600]);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("AI admission completion could not be recorded. TaskId={TaskId}, FailureType={FailureType}", task.Id, ex.GetType().Name);
        }
    }

    private string IdempotencyKey(long userId, string hash) => $"{keyPrefix}:idem:{userId}:{hash}";

    private string ActiveKey(long userId) => $"{keyPrefix}:active:{userId}";

    private string GlobalActiveKey => $"{keyPrefix}:active:global";

    private string DailyImagesKey(long userId, string date) => $"{keyPrefix}:daily:{date}:images:{userId}";

    private string DailyPointsKey(long userId, string date) => $"{keyPrefix}:daily:{date}:points:{userId}";

    private string CircuitKey => $"{keyPrefix}:provider:circuit";

    private static int ResolveDailyTtlSeconds()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var expiry = new DateTimeOffset(now.Date.AddDays(2), TimeSpan.FromHours(8));
        return Math.Max(3600, (int)(expiry - now).TotalSeconds);
    }
}
