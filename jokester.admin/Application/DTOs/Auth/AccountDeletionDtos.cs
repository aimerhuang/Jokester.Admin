namespace jokester.admin.Application.DTOs.Auth;

public sealed class CreateAccountDeletionRequest
{
    public string CurrentPassword { get; init; } = string.Empty;

    public string Confirmation { get; init; } = string.Empty;

    public string ClientRequestId { get; init; } = string.Empty;

    public string? Reason { get; init; }
}

public sealed class AccountDeletionRequestDto
{
    public string RequestId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime RequestedAt { get; init; }

    public DateTime ScheduledDeletionAt { get; init; }

    public bool CanCancel { get; init; }

    public DateTime? CompletedAt { get; init; }
}
