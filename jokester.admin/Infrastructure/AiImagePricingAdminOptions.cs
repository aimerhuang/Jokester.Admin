namespace jokester.admin.Infrastructure;

/// <summary>生图定价后台的调价保护配置。</summary>
public sealed class AiImagePricingAdminOptions
{
    public const string SectionName = "AiImagePricingAdmin";
    /// <summary>单档调价超过此百分比时要求二次确认。</summary>
    public int LargeChangePercent { get; set; } = 50;

    /// <summary>UTC 单日人工价格审计条数超过此值时记录告警。</summary>
    public int DailyAuditWarningThreshold { get; set; } = 500;
}
