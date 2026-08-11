using Cronos;
using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace jokester.admin.Application.Services;

public sealed class PromptLibrarySyncWorker(
    IPromptLibrarySyncQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<PromptLibraryOptions> options,
    ILogger<PromptLibrarySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Prompt library synchronization is disabled.");
            return;
        }

        queue.TryEnqueue(PromptLibrarySyncTrigger.Startup);
        await Task.WhenAll(
            ProcessQueueAsync(stoppingToken),
            ScheduleAsync(stoppingToken));
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var trigger in queue.DequeueAllAsync(cancellationToken))
            {
                queue.MarkStarted();
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<IPromptLibrarySyncRunner>();
                    await runner.RunAsync(trigger, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        "Prompt library synchronization failed outside the runner. Trigger={Trigger}, FailureType={FailureType}",
                        trigger,
                        ex.GetType().Name);
                }
                finally
                {
                    queue.MarkCompleted();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        var cron = CronExpression.Parse(options.Value.SyncCron, CronFormat.IncludeSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            var next = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
            if (!next.HasValue)
            {
                logger.LogError("Prompt library sync cron has no future occurrence. Cron={Cron}", options.Value.SyncCron);
                return;
            }

            var delay = next.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            if (!queue.TryEnqueue(PromptLibrarySyncTrigger.Scheduled))
            {
                logger.LogWarning("Prompt library scheduled sync was skipped because another sync is queued or running.");
            }
        }
    }
}
