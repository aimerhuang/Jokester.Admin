namespace jokester.admin.Application.DTOs.Points;

public sealed record ImageTaskReservationResult(long TaskId, bool Created);

public sealed record ImageTaskBatchReservationResult(IReadOnlyList<long> TaskIds, bool Created);

public sealed record ImageTaskSettlementResult(
    long TaskId,
    long UserId,
    int ImageCount,
    int CompletedImageCount,
    int RefundedPoints,
    bool Transitioned);

public sealed class PointBalanceDto
{
    public int AvailablePoints { get; init; }

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
