using jokester.admin.Application.DTOs.AiImages;

namespace jokester.admin.Application.Abstractions;

public interface IAiImageModelStatusService
{
    /// <summary>Returns the latest snapshot. Never queries ai_image_task except to build the very first snapshot.</summary>
    Task<AiImageModelStatusResponse> GetAsync(CancellationToken cancellationToken);

    /// <summary>Adopts a fresh shared snapshot from Redis, or computes and publishes a new one.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
