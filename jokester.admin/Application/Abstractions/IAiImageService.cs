using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageService
{
    Task<PagedResult<AiImageTaskDto>> GetPageAsync(AiImageQuery query, CancellationToken cancellationToken);

    Task<AiImageParameterOptionsDto> GetParameterOptionsAsync(CancellationToken cancellationToken);

    /// <summary>读取当前全局定价版本。</summary>
    Task<long> GetPricingRevisionAsync(CancellationToken cancellationToken);

    /// <summary>按已创建任务的原始快照返回创建结果。</summary>
    Task<CreateAiImageTasksResponse> BuildCreateTasksResponseAsync(IReadOnlyList<long> ids, string state, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiImagePricingOptionDto>> GetPricingOptionsAsync(CancellationToken cancellationToken);

    Task<ResolveAiImageParametersResponse> ResolveParametersAsync(ResolveAiImageParametersRequest request, CancellationToken cancellationToken);

    Task<AiImageTaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<GenerateAiImageResponse> GenerateAsync(GenerateAiImageRequest request, CancellationToken cancellationToken);

    Task<UploadAiImageResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken);

    Task<GenerateAiImageResponse> GenerateFromResolvedAsync(string prompt, string? modelCode, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, long ownerUserId, CancellationToken cancellationToken);

    Task<GenerateAiImageResponse> GenerateFromResolvedRoutesAsync(string prompt, IReadOnlyList<ResolvedAiImageModelConfig> routes, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, long ownerUserId, CancellationToken cancellationToken);

    /// <summary>使用 GIF 专用路由执行队列任务，返回已校验的媒体地址、尺寸及供应商用量。</summary>
    Task<(string Url, GifTaskResult Result)> GenerateGifFromTaskAsync(AiImageTaskEntity task, IReadOnlyList<ResolvedAiImageModelConfig> routes, CancellationToken cancellationToken);

    Task<long> CreateAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> CreateTasksAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken);

    Task<CreateAiImageTasksResponse> CreateTasksResponseAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken);

    Task SetFavoriteAsync(long id, FavoriteAiImageRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
