using System.Text.Json.Serialization;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.DTOs.AiImages;

public sealed class AiImageQuery : PageQuery
{
    public long? SiteId { get; init; }

    public int? Status { get; init; }

    public bool? IsFavorite { get; init; }

    public string? Prompt { get; init; }

    public string? ModelName { get; init; }

    public DateTime? StartDate { get; init; }

    public DateTime? EndDate { get; init; }
}

public sealed class AiImageTaskDto
{
    /// <summary>任务输出格式。</summary>
    public string OutputFormat { get; init; } = "png";
    /// <summary>动图帧数。</summary>
    public int? FrameCount { get; init; }
    /// <summary>单帧时长（毫秒）。</summary>
    public int? FrameDurationMs { get; init; }
    /// <summary>上游报告的用量，输入未拆分时分类明细为空。</summary>
    public GifUsageDto? Usage { get; init; }
    /// <summary>GIF 结算后的实扣积分。</summary>
    public int? ChargedPoints { get; init; }
    /// <summary>按创建时单价计算的平台费用（人民币），封顶前。</summary>
    public decimal? PlatformCostCny { get; init; }

    /// <summary>预留时实际采用的计价档。</summary>
    public string? PricingTier { get; init; }

    /// <summary>预留时每张图实际扣除的积分。</summary>
    public int? UnitPointCost { get; init; }

    /// <summary>预留时每张图的标准积分价。</summary>
    public int? ListUnitPointCost { get; init; }

    public long Id { get; init; }

    public long TaskId { get; init; }

    public long SiteId { get; init; }

    public long? SourcePromptId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string? SizeContractVersion { get; init; }

    public string? CatalogVersion { get; init; }

    public string? SizeMode { get; init; }

    public string? RequestedSize { get; init; }

    public string? RequestedResolutionCode { get; init; }

    public string? RequestedAspectRatioCode { get; init; }

    public int? RequestedWidth { get; init; }

    public int? RequestedHeight { get; init; }

    public int? OutputWidth { get; init; }

    public int? OutputHeight { get; init; }

    public string? OutputSize { get; init; }

    public string? OutputMimeType { get; init; }

    public int ImageCount { get; init; }

    public int CompletedImageCount { get; init; }

    public int PointCost { get; init; }

    public int BillingStatus { get; init; }

    public int? RefundedPoints { get; init; }

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public string AspectRatioCode { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public string Size { get; init; } = string.Empty;

    public string Quality { get; init; } = string.Empty;

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public string? MaskImageUrl { get; init; }

    public IReadOnlyList<string> ResultUrls { get; init; } = [];

    public IReadOnlyList<string> FavoriteUrls { get; init; } = [];

    public bool IsFavorite { get; init; }

    public string? ErrorMessage { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureStage { get; init; }

    public bool? Retryable { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public string Status { get; init; } = "queued";

    public int StatusCode { get; init; }

    public int Progress { get; init; }

    public int PollAfterSeconds { get; init; }

    public DateTime ExpiresAt { get; init; }

    public IReadOnlyList<AiImageTaskAssetDto> Assets { get; init; } = [];
}

public sealed class AiImageTaskAssetDto
{
    public string Url { get; init; } = string.Empty;
}

public sealed class CreateAiImageTasksResponse
{
    /// <summary>预留时实际采用的计价档。</summary>
    public string? PricingTier { get; init; }

    /// <summary>预留时每张图实际扣除的积分。</summary>
    public int? UnitPointCost { get; init; }

    /// <summary>预留时每张图的标准积分价。</summary>
    public int? ListUnitPointCost { get; init; }

    public long Id { get; init; }

    public long TaskId { get; init; }

    public IReadOnlyList<long> Ids { get; init; } = [];

    public IReadOnlyList<long> TaskIds { get; init; } = [];

    public string RequestState { get; init; } = "active";
}

/// <summary>当前账号对模型的访问权限。</summary>
public sealed record AiImageModelAccess(string Level, bool Available,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReasonCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Basis);

public sealed class AiImageModelOptionDto
{
    /// <summary>模型的访问级别及当前用户的准入依据。</summary>
    public AiImageModelAccess Access { get; set; } = new("all", true, null, null);

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string ProviderCode { get; init; } = string.Empty;

    public string? SizeContractVersion { get; init; }

    public string? CatalogVersion { get; init; }

    public AiImageModelCapabilitiesDto Capabilities { get; init; } = new();

    public IReadOnlyList<string> Resolutions { get; init; } = [];

    public IReadOnlyList<string> Qualities { get; init; } = [];

    public IReadOnlyList<string> AspectRatios { get; init; } = [];

    public int Sort { get; init; }
}

public sealed class AiImageModelCapabilitiesDto
{
    /// <summary>模型支持排队生成动图时提供的参数和价格。</summary>
    public GifCapabilityDto? Gif { get; init; }

    public bool SupportsReferenceImages { get; init; } = true;

    public int MaxReferenceImages { get; init; } = 6;

    public bool SupportsQuality { get; init; }

    public IReadOnlyList<int> SupportedImageCounts { get; init; } = [];

    public IReadOnlyList<string>? SizeModes { get; init; }

    public string? DefaultSizeMode { get; init; }

    public bool? SupportsAutoSize { get; init; }
}

public sealed class AiImageParameterOptionDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? ProviderValue { get; init; }

    public int? ValueInt1 { get; init; }

    public int? ValueInt2 { get; init; }

    public int Sort { get; init; }
}

public sealed class AiImagePointPriceDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public int Points { get; init; }

    /// <summary>已配置的 VIP 优惠价。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? VipPoints { get; init; }

    /// <summary>当前用户实付的单图积分。</summary>
    public int EffectivePoints { get; init; }

    /// <summary>当前用户的计价档。</summary>
    public string PricingTier { get; init; } = "standard";

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

/// <summary>旧协议价格列表及当前全局定价版本。</summary>
public sealed class AiImageLegacyPricingResponse : jokester.admin.Common.ApiResponse<IReadOnlyList<AiImagePricingOptionDto>>
{
    public long PricingRevision { get; init; }
}

public sealed class AiImagePricingOptionDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public int Points { get; init; }

    /// <summary>已配置的 VIP 优惠价。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? VipPoints { get; init; }

    /// <summary>当前用户实付的单图积分。</summary>
    public int EffectivePoints { get; init; }

    /// <summary>当前用户的计价档。</summary>
    public string PricingTier { get; init; } = "standard";

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

public sealed class AiImageCatalogPricingResponse
{
    /// <summary>当前全局定价版本。</summary>
    public long PricingRevision { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string CatalogVersion { get; init; } = string.Empty;

    public IReadOnlyList<AiImageCatalogPricingOptionDto> Items { get; init; } = [];
}

public sealed class AiImageCatalogPricingOptionDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string SizeMode { get; init; } = string.Empty;

    public string? ResolutionCode { get; init; }

    public string QualityCode { get; init; } = string.Empty;

    public int Points { get; init; }

    /// <summary>已配置的 VIP 优惠价。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? VipPoints { get; init; }

    /// <summary>当前用户实付的单图积分。</summary>
    public int EffectivePoints { get; init; }

    /// <summary>当前用户的计价档。</summary>
    public string PricingTier { get; init; } = "standard";

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

public sealed class AiImageParameterOptionsDto
{
    /// <summary>当前全局定价版本。</summary>
    public long PricingRevision { get; init; }

    public IReadOnlyList<AiImageParameterOptionDto> Resolutions { get; init; } = [];

    public IReadOnlyList<AiImageParameterOptionDto> Qualities { get; init; } = [];

    public IReadOnlyList<AiImageParameterOptionDto> AspectRatios { get; init; } = [];

    public IReadOnlyList<AiImagePointPriceDto> PointPrices { get; init; } = [];
}

public sealed class ResolveAiImageParametersRequest
{
    public string? ModelCode { get; init; }

    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string? Resolution { get; init; }

    public string? ResolutionCode { get; init; }

    public string? QualityCode { get; init; }

    public string? AspectRatioCode { get; init; }

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];
}

public sealed class ResolveAiImageParametersResponse
{
    public string? SizeContractVersion { get; init; }

    public string? ModelCode { get; init; }

    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string? RequestedSize { get; init; }

    public string? ResolutionCode { get; init; }

    public string QualityCode { get; init; } = string.Empty;

    public string? AspectRatioCode { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string Size { get; init; } = string.Empty;

    public string ProviderQuality { get; init; } = string.Empty;
}

/// <summary>动图参数、Token 单价及预扣规则。</summary>
public sealed record GifCapabilityDto(int[] FrameCounts, int DefaultFrameCount,
    int[] FrameDurationsMs, int DefaultFrameDurationMs, GifTokenPricingOptions TokenPricing, GifReserveOptions Reserve);

/// <summary>供应商的 Token 用量与输入分类方式。</summary>
public sealed record GifUsageDto(int InputTokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? TextInputTokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ImageInputTokens,
    int OutputTokens, string InputBreakdown);

/// <summary>生图接口共用的输出格式参数。</summary>
public abstract class AiImageOutputRequest
{
    /// <summary>输出格式，默认 png。支持动图的排队入口可提交 gif。</summary>
    public string? OutputFormat { get; init; }
    /// <summary>动图帧数。</summary>
    public int? FrameCount { get; init; }
    /// <summary>动图单帧时长（毫秒）。</summary>
    public int? FrameDurationMs { get; init; }
}

public sealed class CreateAiImageTaskRequest : AiImageOutputRequest
{
    /// <summary>用户确认的每张图片最高积分。</summary>
    public int? ExpectedUnitPoints { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public long SiteId { get; init; }

    public long? SourcePromptId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string? NegativePrompt { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string? Resolution { get; init; }

    public string? ResolutionCode { get; init; }

    public string? QualityCode { get; init; }

    public string? AspectRatioCode { get; init; }

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public IReadOnlyList<string> ReferenceAssetIds { get; init; } = [];

    public string? MaskImageUrl { get; init; }

    public string? MaskAssetId { get; init; }
}

public sealed class GenerateAiImageRequest : AiImageOutputRequest
{
    /// <summary>用户确认的每张图片最高积分。</summary>
    public int? ExpectedUnitPoints { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public long? SourcePromptId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string? Resolution { get; init; }

    public string? ResolutionCode { get; init; }

    public string? QualityCode { get; init; }

    public string? AspectRatioCode { get; init; }

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public IReadOnlyList<string> ReferenceAssetIds { get; init; } = [];

    public string? MaskImageUrl { get; init; }

    public string? MaskAssetId { get; init; }
}

public sealed class UploadAiImageRequest
{
    public IFormFile? File { get; init; }
}

public sealed class FavoriteAiImageRequest
{
    public string ImageUrl { get; init; } = string.Empty;

    public bool IsFavorite { get; init; } = true;
}

public sealed class UploadAiImageResponse
{
    public string AssetId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string ThumbnailUrl { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public long SizeBytes { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool MetadataStripped { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class GenerateAiImageResponse
{
    /// <summary>预留时实际采用的计价档。</summary>
    public string? PricingTier { get; init; }

    /// <summary>预留时每张图实际扣除的积分。</summary>
    public int? UnitPointCost { get; init; }

    /// <summary>预留时每张图的标准积分价。</summary>
    public int? ListUnitPointCost { get; init; }

    public long TaskId { get; init; }

    public IReadOnlyList<long> TaskIds { get; init; } = [];

    public long? SourcePromptId { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string? SizeContractVersion { get; init; }

    public string? CatalogVersion { get; init; }

    public string? SizeMode { get; init; }

    public string RequestState { get; init; } = "active";

    public string? BatchStatus { get; init; }

    public IReadOnlyList<AiImageGenerateResultDto>? Results { get; init; }

    public string ProviderModel { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public string AspectRatioCode { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public string Size { get; init; } = string.Empty;

    public string Quality { get; init; } = string.Empty;

    public string MimeType { get; init; } = "image/png";

    public string Url { get; init; } = string.Empty;

    public IReadOnlyList<string> Urls { get; init; } = [];

    public string Base64 { get; init; } = string.Empty;

    public string DataUrl { get; init; } = string.Empty;

    public string? MaskImageUrl { get; init; }

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public string? RevisedPrompt { get; init; }
}

public sealed class AiImageGenerateResultDto
{
    public int Ordinal { get; init; }

    public long TaskId { get; init; }

    public string Status { get; init; } = "queued";

    public bool IsDeleted { get; init; }

    public string? Url { get; init; }

    public int? OutputWidth { get; init; }

    public int? OutputHeight { get; init; }

    public string? OutputSize { get; init; }

    public string? MimeType { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureStage { get; init; }

    public bool? Retryable { get; init; }

    public int? RefundedPoints { get; init; }
}
