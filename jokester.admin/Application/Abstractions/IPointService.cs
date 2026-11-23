using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IPointService
{
    Task<PointBalanceDto> GetAdminBalanceAsync(long id, CancellationToken cancellationToken);

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

    /// <remarks>
    /// <c>modelSideFailure</c>: for a failed task, whether the failure is attributed to the model and counts toward
    /// the public model status. Pass false for admission, queue, prompt-filter and other user- or platform-side failures.
    /// </remarks>
    Task<ImageTaskSettlementResult> SettleImageTaskAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken,
        bool modelSideFailure = true);

    Task<ImageTaskSettlementResult> SettleVersionedImageTaskAsync(
        long taskId,
        int finalStatus,
        VersionedImageTaskSettlement settlement,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken);

}
