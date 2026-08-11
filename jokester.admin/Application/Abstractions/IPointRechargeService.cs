using jokester.admin.Application.DTOs.Points;

namespace jokester.admin.Application.Abstractions;

public interface IPointRechargeService
{
    Task<IReadOnlyList<RechargePackageDto>> GetPackagesAsync(CancellationToken cancellationToken);

    Task<RechargeOrderDto> CreateOrderAsync(CreateRechargeOrderRequest request, CancellationToken cancellationToken);

    Task<RedeemPointCodeResponse> RedeemAsync(RedeemPointCodeRequest request, CancellationToken cancellationToken);

    Task<IssuedPointRedeemCodesResponse> IssueCodesAsync(IssuePointRedeemCodesRequest request, CancellationToken cancellationToken);
}
