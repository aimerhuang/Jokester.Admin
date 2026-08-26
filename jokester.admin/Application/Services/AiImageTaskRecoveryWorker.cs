using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using jokester.admin.Infrastructure;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskRecoveryWorker(
    IAiImageTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AiCostControlOptions> options,
    ILogger<AiImageTaskRecoveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("AI image task recovery scan failed. FailureType={FailureType}", ex.GetType().Name);
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var pointService = scope.ServiceProvider.GetRequiredService<IPointService>();
        var admissionService = scope.ServiceProvider.GetRequiredService<IAiImageAdmissionService>();
        var now = HongKongNow();

        await RecoverOutboxAsync(db, pointService, admissionService, now, cancellationToken);

        var processingTasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted && x.Status == 3 && x.BillingStatus == 0)
            .OrderBy(x => x.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var task in processingTasks.Where(task => IsProcessingExpired(task, now)))
        {
            if (task.SizeContractVersion == AiImageCatalogService.SizeContractVersion)
            {
                await RecoverVersionedProcessingTaskAsync(db, pointService, admissionService, task, now, cancellationToken);
                continue;
            }
            var resultUrls = DeserializeImageUrls(task.ResultUrls);
            var settlement = await pointService.SettleImageTaskAsync(
                task.Id,
                2,
                task.ResultUrls,
                "AI image generation timed out during worker recovery.",
                resultUrls.Count,
                cancellationToken);
            if (settlement.Transitioned)
            {
                await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
            }
        }

        var configuredCapacity = Math.Min(queue.Capacity, options.Value.MaxQueuedTasks);
        var availableQueueSlots = Math.Max(0, configuredCapacity - queue.BacklogCount);
        if (availableQueueSlots == 0)
        {
            return;
        }

        var pendingTasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted && x.Status == 0 && x.BillingStatus == 0)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Min(availableQueueSlots, 100))
            .ToListAsync(cancellationToken);
        foreach (var task in pendingTasks)
        {
            if (task.SizeContractVersion == AiImageCatalogService.SizeContractVersion)
            {
                var outboxPending = await db.Queryable<AiImageTaskOutboxEntity>()
                    .AnyAsync(x => x.TaskId == task.Id && x.Status == "pending", cancellationToken);
                if (outboxPending)
                {
                    continue;
                }
            }
            if (task.CreatedAt.Add(AiImageTaskProcessor.ResolvePendingTaskTimeout()) <= now)
            {
                if (task.SizeContractVersion == AiImageCatalogService.SizeContractVersion)
                {
                    await FailVersionedTaskAsync(
                        db,
                        pointService,
                        admissionService,
                        task,
                        MachineErrorCodes.ProviderUnavailable,
                        "preflight",
                        true,
                        "AI image task expired before a worker could start it.",
                        null,
                        cancellationToken);
                    continue;
                }
                var settlement = await pointService.SettleImageTaskAsync(
                    task.Id,
                    2,
                    task.ResultUrls,
                    "AI image task expired before a worker could start it.",
                    DeserializeImageUrls(task.ResultUrls).Count,
                    cancellationToken);
                if (settlement.Transitioned)
                {
                    await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
                }
                continue;
            }

            if (!queue.TryQueue(task.Id))
            {
                break;
            }
        }
    }

    private async Task RecoverOutboxAsync(
        ISqlSugarClient db,
        IPointService pointService,
        IAiImageAdmissionService admissionService,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await db.Queryable<AiImageTaskOutboxEntity>()
            .Where(x => x.Status == "pending" && x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var requestGroup in rows.GroupBy(x => x.RequestId))
        {
            if (requestGroup.Min(x => x.CreatedAt)
                .AddMinutes(Math.Max(1, options.Value.OutboxBindDeadlineMinutes)) <= now)
            {
                await FailExpiredOutboxTasksAsync(
                    db,
                    pointService,
                    admissionService,
                    requestGroup.ToArray(),
                    cancellationToken);
                continue;
            }
            var request = await db.Queryable<AiImageRequestEntity>()
                .FirstAsync(x => x.Id == requestGroup.Key, cancellationToken);
            if (request is null
                || string.IsNullOrWhiteSpace(request.AdmissionReservationId)
                || string.IsNullOrWhiteSpace(request.AdmissionQuotaDate))
            {
                await DelayOutboxAsync(db, requestGroup.Select(x => x.Id).ToArray(), now, cancellationToken);
                continue;
            }
            var taskIds = await db.Queryable<AiImageRequestTaskEntity>()
                .Where(x => x.RequestId == request.Id)
                .OrderBy(x => x.TaskOrdinal)
                .Select(x => x.TaskId)
                .ToListAsync(cancellationToken);
            if (taskIds.Count != request.TaskCount || taskIds.Count == 0)
            {
                await DelayOutboxAsync(db, requestGroup.Select(x => x.Id).ToArray(), now, cancellationToken);
                continue;
            }
            var reservation = new AiImageAdmissionReservation(
                request.UserId,
                request.IdempotencyKeyHash,
                request.CanonicalPayloadHash,
                request.AdmissionQuotaDate,
                request.RequestedImageCount,
                request.ReservedPointCost,
                request.AdmissionReservationId,
                false,
                0);
            try
            {
                var tasks = await db.Queryable<AiImageTaskEntity>()
                    .Where(x => taskIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                var taskLookup = tasks.ToDictionary(x => x.Id);
                if (taskLookup.Count != taskIds.Count)
                {
                    await DelayOutboxAsync(db, requestGroup.Select(x => x.Id).ToArray(), now, cancellationToken);
                    continue;
                }
                await admissionService.BindBatchAsync(
                    reservation,
                    request.Id,
                    taskIds.Select((taskId, ordinal) => new AiImageAdmissionTask(
                        taskId,
                        ordinal,
                        taskLookup[taskId].ImageCount,
                        taskLookup[taskId].PointCost)).ToArray(),
                    cancellationToken);
                var pendingTaskIds = requestGroup.Select(x => x.TaskId).ToHashSet();
                foreach (var taskId in taskIds.Where(pendingTaskIds.Contains))
                {
                    if (!queue.TryQueue(taskId))
                    {
                        break;
                    }
                    await db.Updateable<AiImageTaskOutboxEntity>()
                        .SetColumns(x => new AiImageTaskOutboxEntity
                        {
                            Status = "dispatched",
                            UpdatedAt = now
                        })
                        .Where(x => x.RequestId == request.Id && x.TaskId == taskId && x.Status == "pending")
                        .ExecuteCommandAsync(cancellationToken);
                }
            }
            catch (AppException ex)
            {
                logger.LogWarning(
                    "AI image outbox binding is still pending. RequestId={RequestId}, FailureCode={FailureCode}",
                    request.Id,
                    ex.MachineCode);
                await DelayOutboxAsync(db, requestGroup.Select(x => x.Id).ToArray(), now, cancellationToken);
            }
        }
    }

    private static async Task FailExpiredOutboxTasksAsync(
        ISqlSugarClient db,
        IPointService pointService,
        IAiImageAdmissionService admissionService,
        IReadOnlyList<AiImageTaskOutboxEntity> rows,
        CancellationToken cancellationToken)
    {
        var taskIds = rows.Select(x => x.TaskId).Distinct().ToArray();
        var tasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => taskIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            await FailVersionedTaskAsync(
                db,
                pointService,
                admissionService,
                task,
                MachineErrorCodes.ServiceUnavailable,
                "preflight",
                true,
                "AI image task could not be bound to the dispatch queue before its deadline.",
                null,
                cancellationToken);
        }
        var outboxIds = rows.Select(row => row.Id).ToArray();
        await db.Updateable<AiImageTaskOutboxEntity>()
            .SetColumns(x => new AiImageTaskOutboxEntity
            {
                Status = "failed",
                UpdatedAt = HongKongNow()
            })
            .Where(x => outboxIds.Contains(x.Id) && x.Status == "pending")
            .ExecuteCommandAsync(cancellationToken);
    }

    private static async Task DelayOutboxAsync(
        ISqlSugarClient db,
        long[] outboxIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (outboxIds.Length == 0)
        {
            return;
        }
        await db.Updateable<AiImageTaskOutboxEntity>()
            .SetColumns(x => x.AttemptCount == x.AttemptCount + 1)
            .SetColumns(x => new AiImageTaskOutboxEntity
            {
                NextAttemptAt = now.AddSeconds(30),
                UpdatedAt = now
            })
            .Where(x => outboxIds.Contains(x.Id) && x.Status == "pending")
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task RecoverVersionedProcessingTaskAsync(
        ISqlSugarClient db,
        IPointService pointService,
        IAiImageAdmissionService admissionService,
        AiImageTaskEntity task,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(task.ClaimTokenHash) && task.LeaseExpiresAt > now)
        {
            return;
        }
        var attempt = await db.Queryable<AiImageProviderAttemptEntity>()
            .Where(x => x.TaskId == task.Id)
            .OrderByDescending(x => x.Id)
            .FirstAsync(cancellationToken);
        if (attempt is null || attempt.State == "prepared")
        {
            var reset = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 0,
                    ClaimTokenHash = null,
                    LeaseExpiresAt = null,
                    UpdatedAt = now
                })
                .Where(x => x.Id == task.Id && x.Status == 3 && x.BillingStatus == 0
                    && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))
                .ExecuteCommandAsync(cancellationToken);
            if (reset != 1)
            {
                return;
            }
            if (attempt is not null)
            {
                await db.Updateable<AiImageProviderAttemptEntity>()
                    .SetColumns(x => new AiImageProviderAttemptEntity { State = "abandoned", CompletedAt = now })
                    .Where(x => x.Id == attempt.Id && x.State == "prepared")
                    .ExecuteCommandAsync(cancellationToken);
            }
            queue.TryQueue(task.Id);
            return;
        }
        if (attempt.State == "inflight")
        {
            if (attempt.Deadline > now)
            {
                return;
            }
            await db.Updateable<AiImageProviderAttemptEntity>()
                .SetColumns(x => new AiImageProviderAttemptEntity { State = "provider_unknown" })
                .Where(x => x.Id == attempt.Id && x.State == "inflight")
                .ExecuteCommandAsync(cancellationToken);
            attempt.State = "provider_unknown";
        }
        if (attempt.State == "provider_unknown" && attempt.ReconcileBy <= now)
        {
            await FailVersionedTaskAsync(
                db,
                pointService,
                admissionService,
                task,
                MachineErrorCodes.ProviderOutcomeUnknown,
                "provider",
                true,
                "图片服务结果在对账时限内仍无法确认，任务已退款。",
                attempt,
                cancellationToken);
        }
    }

    private static async Task FailVersionedTaskAsync(
        ISqlSugarClient db,
        IPointService pointService,
        IAiImageAdmissionService admissionService,
        AiImageTaskEntity task,
        string failureCode,
        string failureStage,
        bool retryable,
        string message,
        AiImageProviderAttemptEntity? attempt,
        CancellationToken cancellationToken)
    {
        var now = HongKongNow();
        var claimHash = AiImageIdempotency.HashKey(Guid.NewGuid().ToString("N"));
        var claimed = await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity
            {
                Status = 3,
                ClaimTokenHash = claimHash,
                LeaseExpiresAt = now.AddMinutes(2),
                HeartbeatAt = now,
                UpdatedAt = now
            })
            .SetColumns(x => x.ClaimEpoch == x.ClaimEpoch + 1)
            .Where(x => x.Id == task.Id
                && x.BillingStatus == 0
                && (x.Status == 0 || x.Status == 3)
                && (x.ClaimTokenHash == null || x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))
            .ExecuteCommandAsync(cancellationToken);
        if (claimed != 1)
        {
            return;
        }
        var claimedTask = await db.Queryable<AiImageTaskEntity>()
            .FirstAsync(x => x.Id == task.Id && x.ClaimTokenHash == claimHash, cancellationToken);
        if (claimedTask is null)
        {
            return;
        }
        var settlement = await pointService.SettleVersionedImageTaskAsync(
            claimedTask.Id,
            2,
            new VersionedImageTaskSettlement(
                claimedTask.ResultUrls,
                null,
                null,
                null,
                null,
                failureCode,
                failureStage,
                retryable,
                claimedTask.ClaimEpoch,
                claimHash,
                attempt?.AttemptId,
                attempt is null ? "failed" : "reconciled_failed"),
            message,
            0,
            cancellationToken);
        if (settlement.Transitioned)
        {
            await admissionService.CompleteAsync(claimedTask, settlement.CompletedImageCount, settlement.RefundedPoints);
        }
    }

    private static bool IsProcessingExpired(AiImageTaskEntity task, DateTime now)
    {
        var startedAt = task.StartedAt ?? task.CreatedAt;
        return startedAt.Add(AiImageTaskProcessor.ResolveTaskTimeout(task.ImageCount)) <= now;
    }

    private static IReadOnlyList<string> DeserializeImageUrls(string? imageUrls)
    {
        if (string.IsNullOrWhiteSpace(imageUrls))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(imageUrls) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTime HongKongNow() => DateTime.UtcNow.AddHours(8);
}
