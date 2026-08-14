using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Services;

public sealed class AppleNotificationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AppleNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAppleIapService>()
                    .ProcessPendingNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Apple notification retry iteration failed.");
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
