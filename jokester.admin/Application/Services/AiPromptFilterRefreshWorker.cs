using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class AiPromptFilterRefreshWorker(
    IAiPromptFilter promptFilter,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    IOptions<AiPromptFilterOptions> filterOptions,
    ILogger<AiPromptFilterRefreshWorker> logger) : BackgroundService
{
    private RedisChannel _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!filterOptions.Value.Enabled)
        {
            return;
        }

        _channel = RedisChannel.Literal($"{redisOptions.Value.InstanceName}ai-prompt-filter:changed");
        await TryLoadAsync(force: true, stoppingToken);
        await TrySubscribeAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(filterOptions.Value.RefreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryLoadAsync(force: false, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().UnsubscribeAsync(_channel);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Prompt filter Redis unsubscribe failed. FailureType={FailureType}",
                ex.GetType().Name);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task TrySubscribeAsync()
    {
        try
        {
            await redis.GetSubscriber().SubscribeAsync(_channel, (_, _) =>
            {
                _ = Task.Run(() => TryLoadAsync(force: false, CancellationToken.None));
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Prompt filter Redis subscription failed; database polling remains active. FailureType={FailureType}",
                ex.GetType().Name);
        }
    }

    private async Task TryLoadAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            await promptFilter.RefreshAsync(force, publish: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Prompt filter background refresh failed. FailureType={FailureType}",
                ex.GetType().Name);
        }
    }
}
