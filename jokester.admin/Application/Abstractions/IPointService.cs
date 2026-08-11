using jokester.admin.Application.DTOs.Points;
using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IPointService
{
    Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken);

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

    Task<ImageTaskSettlementResult> SettleImageTaskAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken);

}
