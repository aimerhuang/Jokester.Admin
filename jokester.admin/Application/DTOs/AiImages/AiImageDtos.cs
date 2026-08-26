using jokester.admin.Application.DTOs.Common;
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
    public long Id { get; init; }

    public long TaskId { get; init; }

    public IReadOnlyList<long> Ids { get; init; } = [];

    public IReadOnlyList<long> TaskIds { get; init; } = [];

    public string RequestState { get; init; } = "active";
}

public sealed class AiImageModelOptionDto
{
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

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

public sealed class AiImagePricingOptionDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public int Points { get; init; }

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

public sealed class AiImageCatalogPricingResponse
{
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

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Sort { get; init; }
}

public sealed class AiImageParameterOptionsDto
{
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

public sealed class CreateAiImageTaskRequest
{
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

public sealed class GenerateAiImageRequest
{
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
