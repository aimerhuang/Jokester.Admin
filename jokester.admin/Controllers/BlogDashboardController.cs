using jokester.admin.Application.Abstractions;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/blog/dashboard")]
public sealed class BlogDashboardController(IBlogDashboardService dashboardService) : BaseApiController
{
    /// <summary>
    /// 获取博客仪表盘统计。
    /// </summary>
    /// <remarks>
    /// 统计固定读取 blog 站点，包含文章、评论、媒体数量，以及最近 10 条待审核评论。
    /// </remarks>
    [Permission("Blog.Dashboard.View")]
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken)
    {
        var result = await dashboardService.GetStatsAsync(cancellationToken);
        return Success(result);
    }
}
