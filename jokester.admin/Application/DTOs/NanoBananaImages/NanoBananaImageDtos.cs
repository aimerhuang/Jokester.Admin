namespace jokester.admin.Application.DTOs.NanoBananaImages;

public sealed class GenerateNanoBananaImageRequest
{
    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public long? SourcePromptId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string AspectRatioCode { get; init; } = string.Empty;

    public string AspectRatios { get; init; } = string.Empty;

    public string Size { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    public IReadOnlyList<string> ImageAssetIds { get; init; } = [];
}

public sealed class CreateNanoBananaImageTaskRequest
{
    public string? SizeMode { get; init; }

    public string? CatalogVersion { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public long? SourcePromptId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string AspectRatioCode { get; init; } = string.Empty;

    public string AspectRatios { get; init; } = string.Empty;

    public string Size { get; init; } = string.Empty;

    public int ImageCount { get; init; } = 1;

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    public IReadOnlyList<string> ImageAssetIds { get; init; } = [];
}

public sealed class GenerateNanoBananaImageResponse
{
    public long TaskId { get; init; }

    public long? SourcePromptId { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string ModelCode { get; init; } = string.Empty;

    public string ProviderModel { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string Size { get; init; } = string.Empty;

    public string Quality { get; init; } = string.Empty;

    public string MimeType { get; init; } = "image/png";

    public string Url { get; init; } = string.Empty;

    public IReadOnlyList<string> Urls { get; init; } = [];

    public string Base64 { get; init; } = string.Empty;

    public string DataUrl { get; init; } = string.Empty;

    public bool IsImageToImage { get; init; }

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    public string? RevisedPrompt { get; init; }
}
