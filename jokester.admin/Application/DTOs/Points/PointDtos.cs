namespace jokester.admin.Application.DTOs.Points;

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
