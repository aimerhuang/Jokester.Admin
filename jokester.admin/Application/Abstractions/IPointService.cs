using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IPointService
{
    Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken);

    Task<PagedResult<PointDetailDto>> GetDetailsAsync(PageQuery query, CancellationToken cancellationToken);

    Task<SignInPointResponse> SignInAsync(CancellationToken cancellationToken);

    Task<int> GetImageGenerateCostAsync(string modelCode, string resolutionCode, string qualityCode, int imageCount, CancellationToken cancellationToken);

    Task<ImageTaskReservationResult> ReserveImageTaskAsync(
        AiImageTaskEntity task,
        string modelCode,
        string resolutionCode,
        string qualityCode,
        CancellationToken cancellationToken);

    Task<ImageTaskBatchReservationResult> ReserveImageTasksAsync(
        IReadOnlyList<AiImageTaskEntity> tasks,
        string modelCode,
        string resolutionCode,
        string qualityCode,
        CancellationToken cancellationToken);

    Task<VersionedImageTaskBatchReservationResult> ReserveVersionedImageTasksAsync(
        AiImageRequestEntity request,
        IReadOnlyList<AiImageTaskEntity> tasks,
        IReadOnlyList<AiImageTaskInputEntity> inputs,
        long priceId,
        CancellationToken cancellationToken);

    Task<ImageTaskSettlementResult> SettleImageTaskAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken);

    Task<ImageTaskSettlementResult> SettleVersionedImageTaskAsync(
        long taskId,
        int finalStatus,
        VersionedImageTaskSettlement settlement,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken);

}
