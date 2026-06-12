using jokester.admin.Application.Abstractions;
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
    public async Task<IActionResult> GetBalance(CancellationToken cancellationToken)
    {
        var result = await pointService.GetBalanceAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 每日签到领取积分
    /// </summary>
    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn(CancellationToken cancellationToken)
    {
        var result = await pointService.SignInAsync(cancellationToken);
        return Success(result, "签到成功");
    }
}
