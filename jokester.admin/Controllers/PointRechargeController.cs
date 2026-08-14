using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/points/recharge")]
public sealed class PointRechargeController(
    IPointRechargeService rechargeService,
    IAppleIapService appleIapService) : BaseApiController
{
    /// <summary>
    /// 查询当前可购买的积分套餐。
    /// </summary>
    [HttpGet("packages")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RechargePackageDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackages([FromQuery] string? platform, CancellationToken cancellationToken)
    {
        var result = await rechargeService.GetPackagesAsync(platform, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 校验并履约 StoreKit 消耗型交易。
    /// </summary>
    [HttpPost("apple/transactions")]
    [ProducesResponseType(typeof(ApiResponse<AppleTransactionFulfillmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FulfillAppleTransaction(
        [FromBody] FulfillAppleTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
        var result = await appleIapService.FulfillAsync(request, idempotencyKey, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 创建待支付充值订单。支付完成后由管理员或支付回调发放兑换码。
    /// </summary>
    [HttpPost("orders")]
    [ProducesResponseType(typeof(ApiResponse<RechargeOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateRechargeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await rechargeService.CreateOrderAsync(request, cancellationToken);
        return Success(result, "充值订单已创建");
    }

    /// <summary>
    /// 核销一次性兑换码并增加当前用户积分。
    /// </summary>
    [HttpPost("redeem")]
    [ProducesResponseType(typeof(ApiResponse<RedeemPointCodeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Redeem(
        [FromBody] RedeemPointCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await rechargeService.RedeemAsync(request, cancellationToken);
        return Success(result, "积分兑换成功");
    }

    /// <summary>
    /// 超级管理员按套餐或自定义积分批量签发兑换码；传 orderNo 时同时确认并履约该套餐订单。
    /// </summary>
    [HttpPost("admin/codes")]
    public async Task<IActionResult> IssueCodes(
        [FromBody] IssuePointRedeemCodesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await rechargeService.IssueCodesAsync(request, cancellationToken);
        return Success(result, "兑换码已签发，请立即安全保存，服务端不保存明文");
    }
}
