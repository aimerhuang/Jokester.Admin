namespace jokester.admin.Application.DTOs.Points;

public sealed record ImageTaskReservationResult(long TaskId, bool Created);

public sealed record ImageTaskBatchReservationResult(IReadOnlyList<long> TaskIds, bool Created);

public sealed record VersionedImageTaskBatchReservationResult(
    long RequestId,
    IReadOnlyList<long> TaskIds,
    bool Created);

public sealed record ImageTaskSettlementResult(
    long TaskId,
    long UserId,
    int ImageCount,
    int CompletedImageCount,
    int RefundedPoints,
    bool Transitioned);

public sealed record VersionedImageTaskSettlement(
    string? ResultUrls,
    int? OutputWidth,
    int? OutputHeight,
    string? OutputSize,
    string? OutputMimeType,
    string? FailureCode,
    string? FailureStage,
    bool? Retryable,
    long ClaimEpoch,
    string ClaimTokenHash,
    string? ProviderAttemptId,
    string ProviderAttemptState);

public sealed class PointBalanceDto
{
    public int AvailablePoints { get; init; }

    public int PermanentPoints { get; init; }

    public int ExpiringPoints { get; init; }

    public int NextExpiringPoints { get; init; }

    public DateTime? NextExpireAt { get; init; }

    public bool HasSignedInToday { get; init; }

    public int TodaySignInPoints { get; init; }
}

public sealed class SignInPointResponse
{
    public int Points { get; init; }

    public DateTime ExpireAt { get; init; }

    public int AvailablePoints { get; init; }
}

public sealed class PointDetailDto
{
    public long Id { get; init; }

    public int ChangePoints { get; init; }

    public int BalanceAfter { get; init; }

    public string ChangeType { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string? Remark { get; init; }

    public DateTime CreatedAt { get; init; }
}
