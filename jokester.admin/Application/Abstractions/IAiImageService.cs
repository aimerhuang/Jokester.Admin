using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageService
{
    Task<PagedResult<AiImageTaskDto>> GetPageAsync(AiImageQuery query, CancellationToken cancellationToken);

    Task<AiImageParameterOptionsDto> GetParameterOptionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AiImagePricingOptionDto>> GetPricingOptionsAsync(CancellationToken cancellationToken);

    Task<ResolveAiImageParametersResponse> ResolveParametersAsync(ResolveAiImageParametersRequest request, CancellationToken cancellationToken);

    Task<AiImageTaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<GenerateAiImageResponse> GenerateAsync(GenerateAiImageRequest request, CancellationToken cancellationToken);

    Task<UploadAiImageResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken);

    Task<GenerateAiImageResponse> GenerateFromResolvedAsync(string prompt, string? modelCode, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, CancellationToken cancellationToken);

    Task<long> CreateAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> CreateTasksAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken);

    Task SetFavoriteAsync(long id, FavoriteAiImageRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
