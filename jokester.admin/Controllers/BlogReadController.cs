using jokester.admin.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Route("api/blog")]
public sealed class BlogReadController(IBlogReadService readService) : BaseApiController
{
    /// <summary>
    /// 获取博客汇总信息。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        return Success(await readService.GetSummaryAsync(cancellationToken));
    }

    /// <summary>
    /// 获取最新博客标题。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("titles/latest")]
    public async Task<IActionResult> LatestTitles(
        [FromQuery(Name = "n")] int n = 10,
        CancellationToken cancellationToken = default)
    {
        return Success(await readService.GetLatestTitlesAsync(n, cancellationToken));
    }

    /// <summary>
    /// 获取最新评论。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("comments/latest")]
    public async Task<IActionResult> LatestComments(
        [FromQuery(Name = "n")] int n = 10,
        CancellationToken cancellationToken = default)
    {
        return Success(await readService.GetLatestCommentsAsync(n, cancellationToken));
    }

    /// <summary>
    /// 获取博客网站信息。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("site/info")]
    public async Task<IActionResult> SiteInfo(CancellationToken cancellationToken)
    {
        return Success(await readService.GetSiteInfoAsync(cancellationToken));
    }
}
