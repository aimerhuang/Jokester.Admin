using jokester.admin.Application.DTOs.Points;

namespace jokester.admin.Application.Abstractions;

public interface IPointService
{
    Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken);

    Task<SignInPointResponse> SignInAsync(CancellationToken cancellationToken);

    Task<int> GetImageGenerateCostAsync(string modelCode, string resolutionCode, string qualityCode, int imageCount, CancellationToken cancellationToken);

    Task ConsumeForImageAsync(long userId, long taskId, string modelCode, string resolutionCode, string qualityCode, int points, CancellationToken cancellationToken);

    Task RefundForImageAsync(long userId, long taskId, int points, CancellationToken cancellationToken);
}
