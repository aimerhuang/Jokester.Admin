using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

/// <summary>
/// Super-administrator management of <c>ai_image_model_config</c> routes. API keys are write-only.
/// </summary>
public interface IAiImageModelRouteAdminService
{
    Task<IReadOnlyList<AiImageModelRouteGroupDto>> GetListAsync(AiImageModelRouteQuery query, CancellationToken cancellationToken);

    Task<AiImageModelRouteDto> GetAsync(long id, CancellationToken cancellationToken);

    Task<AiImageModelRouteDto> CreateAsync(CreateAiImageModelRouteRequest request, CancellationToken cancellationToken);

    Task<AiImageModelRouteDto> UpdateAsync(long id, UpdateAiImageModelRouteRequest request, CancellationToken cancellationToken);

    Task<AiImageModelRouteDto> UpdateStatusAsync(long id, UpdateAiImageModelRouteStatusRequest request, CancellationToken cancellationToken);

    Task<AiImageModelRouteProbeResult> ProbeAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<AiImageModelRouteAuditDto>> GetAuditsAsync(AiImageModelRouteAuditQuery query, CancellationToken cancellationToken);
}
