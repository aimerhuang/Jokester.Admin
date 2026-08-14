namespace jokester.admin.Application.DTOs.Points;

public sealed class FulfillAppleTransactionRequest
{
    public string TransactionId { get; init; } = string.Empty;

    public string ProductId { get; init; } = string.Empty;

    public string? AppAccountToken { get; init; }
}

public sealed class AppleTransactionFulfillmentDto
{
    public string TransactionId { get; init; } = string.Empty;

    public string OrderNo { get; init; } = string.Empty;

    public string Status { get; init; } = "fulfilled";

    public string ProductId { get; init; } = string.Empty;

    public int AddedPoints { get; init; }

    public int AvailablePoints { get; init; }

    public DateTime FulfilledAt { get; init; }
}

public sealed class AppleServerNotificationRequest
{
    public string SignedPayload { get; init; } = string.Empty;
}

public sealed class AppleServerNotificationResponse
{
    public string NotificationUuid { get; init; } = string.Empty;

    public string Status { get; init; } = "accepted";
}
