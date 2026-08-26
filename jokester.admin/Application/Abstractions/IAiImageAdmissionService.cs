using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageAdmissionService
{
    Task<AiImageAdmissionReservation> ReserveAsync(
        long userId,
        string idempotencyKeyHash,
        string requestFingerprint,
        int imageCount,
        int pointCost,
        CancellationToken cancellationToken);

    Task BindTaskAsync(AiImageAdmissionReservation reservation, long taskId, CancellationToken cancellationToken);

    Task BindBatchAsync(
        AiImageAdmissionReservation reservation,
        long requestId,
        IReadOnlyList<AiImageAdmissionTask> tasks,
        CancellationToken cancellationToken);

    Task CancelAsync(AiImageAdmissionReservation reservation);

    Task CompleteAsync(AiImageTaskEntity task, int completedImageCount, int refundedPoints);
}

public sealed record AiImageAdmissionReservation(
    long UserId,
    string IdempotencyKeyHash,
    string RequestFingerprint,
    string QuotaDate,
    int ImageCount,
    int PointCost,
    string OwnerToken,
    bool IsDuplicate,
    long ExistingTaskId);

public sealed record AiImageAdmissionTask(long TaskId, int Ordinal, int ImageCount, int PointCost);
