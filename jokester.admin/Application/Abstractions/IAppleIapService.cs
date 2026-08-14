using jokester.admin.Application.DTOs.Points;

namespace jokester.admin.Application.Abstractions;

public interface IAppleIapService
{
    Task<AppleTransactionFulfillmentDto> FulfillAsync(
        FulfillAppleTransactionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AppleServerNotificationResponse> ReceiveNotificationAsync(
        AppleServerNotificationRequest request,
        CancellationToken cancellationToken);

    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken);
}
