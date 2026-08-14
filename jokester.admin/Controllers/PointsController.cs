using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/points")]
public sealed class PointsController(IPointService pointService) : BaseApiController
{
    /// <summary>
    /// 查询当前用户积分余额和今日签到状态
    /// </summary>
    [HttpGet("balance")]
    [ProducesResponseType(typeof(ApiResponse<PointBalanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBalance(CancellationToken cancellationToken)
    {
        var result = await pointService.GetBalanceAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询当前用户积分明细
    /// </summary>
    [HttpGet("details")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PointDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetails(
        [FromQuery] PageQuery query,
        CancellationToken cancellationToken)
    {
        var result = await pointService.GetDetailsAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 每日签到领取积分
    /// </summary>
    [HttpPost("sign-in")]
    [ProducesResponseType(typeof(ApiResponse<SignInPointResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SignIn(CancellationToken cancellationToken)
    {
        var result = await pointService.SignInAsync(cancellationToken);
        return Success(result, "签到成功");
    }
}
