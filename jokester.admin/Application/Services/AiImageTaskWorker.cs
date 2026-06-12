using jokester.admin.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskWorker(
    IAiImageTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AiImageTaskWorker> logger) : BackgroundService
{
    private const int MaxConcurrency = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
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
            logger.LogError(ex, "AI image task {TaskId} failed in background worker.", taskId);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
