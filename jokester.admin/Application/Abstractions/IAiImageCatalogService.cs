using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageCatalogService
{
    Task<IReadOnlyList<AiImageModelOptionDto>> GetModelsAsync(AiImageClientContext client, CancellationToken cancellationToken);

    Task<AiImageCatalogPricingResponse> GetPricingAsync(
        string? modelCode,
        string? catalogVersion,
        AiImageClientContext client,
        CancellationToken cancellationToken);

    Task<AiImageCatalogResolution> ResolveAsync(
        ResolveAiImageParametersRequest request,
        AiImageClientContext client,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ResolvedAiImageModelConfig>> ResolveRoutesAsync(
        long modelReleaseId,
        string sizeMode,
        string? resolutionCode,
        CancellationToken cancellationToken);
}

public sealed record AiImageCatalogResolution(
    AiImageModelReleaseEntity Release,
    AiImageModelReleasePriceEntity Price,
    ResolveAiImageParametersResponse Parameters,
    IReadOnlyList<ResolvedAiImageModelConfig> Routes,
    IReadOnlyList<string> ConsentProviderCodes);
