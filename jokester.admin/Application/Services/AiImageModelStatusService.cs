using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed record AiImageModelHealthSample(bool Success, DateTime CreatedAt, DateTime CompletedAt);

/// <summary>
/// Singleton holding an immutable status snapshot. Status is display-only, so every failure path is fail-open:
/// Redis errors fall back to the in-process snapshot, and a stale snapshot is still served with its generatedAt.
/// </summary>
public sealed class AiImageModelStatusService(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    IOptions<AiImageModelStatusOptions> options,
    ILogger<AiImageModelStatusService> logger) : IAiImageModelStatusService
{
    private static readonly TimeSpan RedisSnapshotTtl = TimeSpan.FromSeconds(120);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AiImageModelStatusResponse? _snapshot;

    private string SnapshotKey => $"{redisOptions.Value.InstanceName}ai-model-status:snapshot";

    public async Task<AiImageModelStatusResponse> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
        {
            return snapshot;
        }

        await RefreshAsync(cancellationToken);
        return Volatile.Read(ref _snapshot) ?? Empty(DateTime.UtcNow);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var shared = await TryReadSharedAsync();
            // Adopt another instance's snapshot only while it is younger than half the refresh interval.
            // Comparing against the full interval makes the timer tick land a few milliseconds early and
            // skip every other refresh, so the effective period silently doubles.
            if (shared is not null && now - shared.GeneratedAt < SharedSnapshotFreshness(options.Value))
            {
                Volatile.Write(ref _snapshot, shared);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var modelConfigService = scope.ServiceProvider.GetRequiredService<IAiImageModelConfigService>();
            var computed = await ComputeAsync(db, modelConfigService, options.Value, now, cancellationToken);
            Volatile.Write(ref _snapshot, computed);
            await TryWriteSharedAsync(computed);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public static async Task<AiImageModelStatusResponse> ComputeAsync(
        ISqlSugarClient db,
        IAiImageModelConfigService modelConfigService,
        AiImageModelStatusOptions options,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var models = (await modelConfigService.GetEnabledModelsAsync(cancellationToken))
            .Where(x => x.Resolutions.Count > 0)
            .ToArray();
        // ai_image_task timestamps are stored as Hong Kong time (UTC+8).
        var since = ToHongKong(utcNow).AddHours(-options.WindowHours);
        var items = new List<AiImageModelStatusItemDto>(models.Length);
        var restrictedCodes = models.Length == 0 ? new List<string>() : await db.Queryable<AiImageModelPolicyEntity>()
            .Where(x => x.AccessLevel == "vip_only").Select(x => x.ModelCode).ToListAsync(cancellationToken);
        var restricted = restrictedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            var modelCode = model.Code;
            // Soft-deleted tasks are intentionally included: deleting history must not change model statistics.
            var rows = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.HealthModelCode == modelCode
                    && x.CompletedAt != null
                    && x.CompletedAt >= since
                    && x.HealthSample != null)
                .OrderBy(x => x.CompletedAt, OrderByType.Desc)
                .OrderBy(x => x.Id, OrderByType.Desc)
                .Take(options.SampleLimit)
                .Select(x => new { x.HealthSample, x.CreatedAt, x.CompletedAt })
                .ToListAsync(cancellationToken);
            var samples = rows
                .Select(x => new AiImageModelHealthSample(
                    x.HealthSample == AiImageModelHealth.Success,
                    x.CreatedAt,
                    x.CompletedAt!.Value))
                .ToArray();
            items.Add(BuildItem(modelCode, model.Name, samples, options, restricted.Contains(modelCode)));
        }

        return new AiImageModelStatusResponse
        {
            GeneratedAt = utcNow,
            WindowHours = options.WindowHours,
            SampleLimit = options.SampleLimit,
            Items = items
        };
    }

    /// <summary>Builds one card from samples ordered newest first and already limited to the time window.</summary>
    public static AiImageModelStatusItemDto BuildItem(
        string modelCode,
        string modelName,
        IReadOnlyList<AiImageModelHealthSample> newestFirst,
        AiImageModelStatusOptions options, bool vipOnly = false)
    {
        var chronological = newestFirst.Take(options.SampleLimit).Reverse().ToArray();
        var sampleCount = chronological.Length;
        var successes = chronological.Where(x => x.Success).ToArray();
        var durations = successes
            .Select(x => (int)Math.Round(Math.Max(0, (x.CompletedAt - x.CreatedAt).TotalSeconds), MidpointRounding.AwayFromZero))
            .ToArray();
        var known = sampleCount >= options.MinSampleCount;
        int? availability = known
            ? (int)Math.Round(successes.Length * 100d / sampleCount, MidpointRounding.AwayFromZero)
            : null;
        var state = availability switch
        {
            null => AiImageModelStatusStates.Unknown,
            var value when value >= options.NormalThresholdPercent => AiImageModelStatusStates.Normal,
            var value when value >= options.DegradedThresholdPercent => AiImageModelStatusStates.Degraded,
            _ => AiImageModelStatusStates.Down
        };

        return new AiImageModelStatusItemDto
        {
            ModelCode = modelCode,
            ModelName = modelName,
            VipOnly = vipOnly,
            State = state,
            Availability = availability,
            SampleCount = sampleCount,
            FastestSuccessSeconds = known && durations.Length > 0 ? durations.Min() : null,
            SlowestSuccessSeconds = known && durations.Length > 0 ? durations.Max() : null,
            Recent = chronological.Select(x => x.Success ? 1 : 0).ToArray(),
            RecentCompletedAt = chronological.Select(x => FromHongKong(x.CompletedAt)).ToArray(),
            LastSampleAt = sampleCount == 0 ? null : FromHongKong(chronological[^1].CompletedAt)
        };
    }

    private async Task<AiImageModelStatusResponse?> TryReadSharedAsync()
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(SnapshotKey);
            if (value.IsNullOrEmpty)
            {
                return null;
            }
            var snapshot = JsonSerializer.Deserialize<AiImageModelStatusResponse>(value.ToString(), JsonOptions);
            return snapshot is null
                ? null
                : new AiImageModelStatusResponse
                {
                    GeneratedAt = DateTime.SpecifyKind(snapshot.GeneratedAt.ToUniversalTime(), DateTimeKind.Utc),
                    WindowHours = snapshot.WindowHours,
                    SampleLimit = snapshot.SampleLimit,
                    Items = snapshot.Items
                };
        }
        catch (Exception ex) when (ex is RedisException or JsonException or TimeoutException)
        {
            logger.LogWarning("AI model status snapshot could not be read from Redis. FailureType={FailureType}", ex.GetType().Name);
            return null;
        }
    }

    private async Task TryWriteSharedAsync(AiImageModelStatusResponse snapshot)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(
                SnapshotKey,
                JsonSerializer.Serialize(snapshot, JsonOptions),
                RedisSnapshotTtl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning("AI model status snapshot could not be written to Redis. FailureType={FailureType}", ex.GetType().Name);
        }
    }

    public static TimeSpan SharedSnapshotFreshness(AiImageModelStatusOptions options) =>
        TimeSpan.FromSeconds(options.RefreshSeconds / 2d);

    private AiImageModelStatusResponse Empty(DateTime utcNow) => new()
    {
        GeneratedAt = utcNow,
        WindowHours = options.Value.WindowHours,
        SampleLimit = options.Value.SampleLimit,
        Items = []
    };

    private static DateTime ToHongKong(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified).AddHours(8);

    private static DateTime FromHongKong(DateTime hongKong) =>
        DateTime.SpecifyKind(hongKong.AddHours(-8), DateTimeKind.Utc);
}

public sealed class AiImageModelStatusWorker(
    IAiImageModelStatusService statusService,
    IOptions<AiImageModelStatusOptions> options,
    ILogger<AiImageModelStatusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.RefreshSeconds));
        do
        {
            try
            {
                await statusService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("AI model status refresh failed. FailureType={FailureType}", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
