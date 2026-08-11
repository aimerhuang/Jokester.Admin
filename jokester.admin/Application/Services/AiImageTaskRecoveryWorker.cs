using System.Text.Json;
using jokester.admin.Application.Abstractions;
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

        var processingTasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted && x.Status == 3 && x.BillingStatus == 0)
            .OrderBy(x => x.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var task in processingTasks.Where(task => IsProcessingExpired(task, now)))
        {
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
            if (task.CreatedAt.Add(AiImageTaskProcessor.ResolvePendingTaskTimeout()) <= now)
            {
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
