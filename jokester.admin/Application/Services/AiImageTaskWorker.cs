using jokester.admin.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using jokester.admin.Infrastructure;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskWorker(
    IAiImageTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AiCostControlOptions> options,
    ILogger<AiImageTaskWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var semaphore = new SemaphoreSlim(options.Value.MaxGlobalProviderConcurrency);
        var runningTasks = new HashSet<Task>();

        try
        {
            await foreach (var taskId in queue.DequeueAllAsync(stoppingToken))
            {
                await semaphore.WaitAsync(stoppingToken);

                var runningTask = ProcessTaskAsync(taskId, semaphore, stoppingToken);
                runningTasks.Add(runningTask);
                runningTasks.RemoveWhere(x => x.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (runningTasks.Count > 0)
            {
                await Task.WhenAll(runningTasks);
            }
        }
    }

    private async Task ProcessTaskAsync(long taskId, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IAiImageTaskProcessor>();
            await processor.ProcessAsync(taskId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(
                "AI image task failed in background worker. TaskId={TaskId}, FailureType={FailureType}",
                taskId,
                ex.GetType().Name);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
