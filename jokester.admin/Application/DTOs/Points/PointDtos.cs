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
