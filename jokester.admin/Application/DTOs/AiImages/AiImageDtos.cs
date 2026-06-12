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

    public long SiteId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ImageCount { get; init; }

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

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public int Status { get; init; }
}

public sealed class AiImageModelOptionDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public int Sort { get; init; }
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
    public string Resolution { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = "med";

    public string AspectRatioCode { get; init; } = "1:1";

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];
}

public sealed class ResolveAiImageParametersResponse
{
    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = string.Empty;

    public string AspectRatioCode { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public string Size { get; init; } = string.Empty;

    public string ProviderQuality { get; init; } = string.Empty;
}

public sealed class CreateAiImageTaskRequest
{
    public long SiteId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string? NegativePrompt { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public string Resolution { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = "med";

    public string AspectRatioCode { get; init; } = "1:1";

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public string? MaskImageUrl { get; init; }
}

public sealed class GenerateAiImageRequest
{
    public string Prompt { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public string Resolution { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string QualityCode { get; init; } = "med";

    public string AspectRatioCode { get; init; } = "1:1";

    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    public string? MaskImageUrl { get; init; }
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
    public string Url { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public long FileSize { get; init; }
}

public sealed class GenerateAiImageResponse
{
    public long TaskId { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

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
