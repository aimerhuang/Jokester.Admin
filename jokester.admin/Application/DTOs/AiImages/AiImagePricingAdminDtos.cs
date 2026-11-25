using System.Text.Json.Serialization;
using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.AiImages;

/// <summary>定价后台设置和各模型价格矩阵。</summary>
public sealed record PricingOverview(int DefaultVipDiscountPercent, int Revision, long PricingRevision,
    int LargeChangePercent, IReadOnlyList<PricingModel> Models);

/// <summary>模型路由、准入策略、折扣和可编辑价格。</summary>
public sealed record PricingModel(string ModelCode, string ModelName, string ProviderProtocol,
    string SizeContractVersion, int EnabledRouteCount, string AccessLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? VipDiscountPercent,
    int EffectiveDiscountPercent, int Revision, IReadOnlyList<PricingRow> Prices,
    IReadOnlyList<PricingSlot> MissingPrices, IReadOnlyList<ReleasePricingRow> ReleasePrices);

/// <summary>一个按分辨率及画质计价的组合。Nano 的画质为空字符串。</summary>
public sealed record PricingSlot(string ResolutionCode, string QualityCode);

/// <summary>当前发布版本的只读定价。</summary>
public sealed record ReleasePricingRow(string SizeMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ResolutionCode, string QualityCode, int Points);

/// <summary>标准积分价、VIP 积分价和行版本。</summary>
public sealed record PricingRow(long Id, string ModelCode, string ResolutionCode, string QualityCode,
    int Points, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? VipPoints, string VipPriceMode, decimal PriceAmount, int Status, int Revision);

/// <summary>新增标准价格档，VIP 价默认跟随模型有效折扣。</summary>
public sealed record CreatePricingRow(string? ModelCode, string? ResolutionCode, string? QualityCode,
    int Points, decimal PriceAmount, string? Reason, int Status = 1);

/// <summary>价格档编辑值。省略折算金额或启用状态时保留原值；手动档必须显式提交 <c>vipPoints</c>，<c>null</c> 表示该档无 VIP 优惠。</summary>
public sealed record EditPricingRow
{
    private readonly int? vipPoints;
    private readonly bool hasVipPoints;

    public long Id { get; init; }
    public int? Revision { get; init; }
    public int Points { get; init; }
    public string? VipPriceMode { get; init; }

    /// <summary>手动 VIP 价。<c>vipPriceMode=manual</c> 时必须出现在请求中，<c>null</c> 表示无优惠；跟随模式忽略。</summary>
    public int? VipPoints
    {
        get => vipPoints;
        init { vipPoints = value; hasVipPoints = true; }
    }

    /// <summary>请求中是否出现了 <c>vipPoints</c>（包括显式 <c>null</c>）。</summary>
    [JsonIgnore]
    public bool HasVipPoints => hasVipPoints;

    public decimal? PriceAmount { get; init; }
    public int? Status { get; init; }
}

/// <summary>原子批量调价请求。</summary>
public sealed record BatchPricingRequest(string? Reason, IReadOnlyList<EditPricingRow>? Items,
    bool ConfirmLargeChange = false, bool ConfirmDisableLastPrice = false);

/// <summary>模型编码为空时预览默认折扣，模型折扣为空时恢复跟随默认。</summary>
public sealed record DiscountPreviewRequest(string? ModelCode, int? DiscountPercent);

/// <summary>用预览凭据确认折扣变更。</summary>
public sealed record ApplyDiscountRequest(int? DiscountPercent, string? PreviewToken, string? Reason);

/// <summary>带模型策略行版本的访问策略变更。</summary>
public sealed record ChangeAccessRequest(string? AccessLevel, int? Revision, string? Reason);

/// <summary>折扣变更对单个价格档的影响。</summary>
public sealed record DiscountPreviewRow(long Id, string ModelCode, string ResolutionCode, string QualityCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? OldVipPoints,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? NewVipPoints);

/// <summary>参与重算的自动档、保留手动价的档位及本次预览凭据。</summary>
public sealed record DiscountPreview(string PreviewToken, int ModelCount, int PriceCount,
    IReadOnlyList<DiscountPreviewRow> Changes, IReadOnlyList<DiscountPreviewRow> ManualPrices);

/// <summary>写入后的全局定价版本及受影响价格档。</summary>
public sealed record PricingWriteResult(long PricingRevision, IReadOnlyList<PricingRow> Prices);

/// <summary>定价审计分页及筛选条件，时间使用 UTC。</summary>
public sealed class PricingAuditQuery : PageQuery
{
    /// <summary>模型编码。</summary>
    public string? ModelCode { get; init; }
    /// <summary>操作者 ID，0 为系统。</summary>
    public long? OperatorUserId { get; init; }
    /// <summary>开始时间，含边界。</summary>
    public DateTimeOffset? StartTime { get; init; }
    /// <summary>截止时间，含边界。</summary>
    public DateTimeOffset? EndTime { get; init; }
    /// <summary>按 points、vipPoints、discount 或 access 筛选。</summary>
    public string? ChangeType { get; init; }
}

/// <summary>变更原因、操作者及变更前后的字段。</summary>
public sealed record PricingAuditRow(long Id, string BatchId, string TargetType, string TargetId,
    string? ModelCode, string? ResolutionCode, string? QualityCode, string Action, string? BeforeJson,
    string? AfterJson, string Reason, long OperatorUserId, string? OperatorUserName, DateTime CreatedAt);
