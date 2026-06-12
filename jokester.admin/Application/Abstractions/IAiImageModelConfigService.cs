using jokester.admin.Application.DTOs.AiImages;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageModelConfigService
{
    Task<IReadOnlyList<AiImageModelOptionDto>> GetEnabledModelsAsync(CancellationToken cancellationToken);

    Task<ResolvedAiImageModelConfig> ResolveAsync(string? modelCode, string? resolutionCode, CancellationToken cancellationToken);
}

public sealed class ResolvedAiImageModelConfig
{
    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string ProviderModel { get; init; } = string.Empty;

    public string? ResolutionCode { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string TextToImagePath { get; init; } = "/images/generations";

    public string ImageToImagePath { get; init; } = "/images/edits";
}
