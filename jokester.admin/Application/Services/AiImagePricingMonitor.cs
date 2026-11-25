using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

/// <summary>巡检启用路由缺价和单日人工定价审计异常，在状态变化时记录告警。</summary>
public sealed class AiImagePricingMonitor(IServiceScopeFactory scopes, IOptions<AiImagePricingAdminOptions> options,
    ILogger<AiImagePricingMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        var previous = new HashSet<string>();
        DateTime? lastAuditWarning = null;
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                var day = DateTime.UtcNow.Date;
                var missing = await db.Queryable<AiImageModelConfigEntity>()
                    .Where(r => r.Status == 1 && !r.IsDeleted && !SqlFunc.Subqueryable<AiImagePointPriceEntity>()
                        .Where(p => p.ModelCode == r.ModelCode && p.Status == 1 && !p.IsDeleted && p.Points > 0 &&
                            (r.ResolutionCode == "" || r.ResolutionCode == null || p.ResolutionCode == r.ResolutionCode)).Any())
                    .Select(r => new { r.ModelCode, r.ResolutionCode }).Distinct().ToListAsync(stoppingToken);
                var current = missing.Select(r => $"{r.ModelCode}/{r.ResolutionCode}").ToHashSet();
                foreach (var route in current.Except(previous))
                    logger.LogWarning("AI_PRICING_ROUTE_UNPRICED Enabled route {Route} has no active legacy price", route);
                previous = current;
                var nextDay = day.AddDays(1);
                var count = await db.Queryable<AiImagePricingAuditEntity>()
                    .Where(a => a.OperatorUserId != 0 && a.CreatedAt >= day && a.CreatedAt < nextDay)
                    .CountAsync(stoppingToken);
                if (count > options.Value.DailyAuditWarningThreshold && lastAuditWarning != day)
                {
                    logger.LogWarning("AI_PRICING_AUDIT_VOLUME {AuditCount} manual pricing audit rows since {UtcDate}", count, day);
                    lastAuditWarning = day;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("AI pricing monitor could not read pricing configuration. FailureType={FailureType}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
