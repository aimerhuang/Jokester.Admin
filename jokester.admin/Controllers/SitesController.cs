using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Sites;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
public sealed class SitesController(ISiteService siteService) : BaseApiController
{
    /// <summary>
    /// 获取所有站点编码。
    /// </summary>
    /// <remarks>
    /// 公开接口，不需要登录；返回所有未删除站点及启用状态。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("site_code")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await siteService.GetAllAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询站点。
    /// </summary>
    [Permission("System.Site.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] SiteQuery query, CancellationToken cancellationToken)
    {
        var result = await siteService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 新增站点。
    /// </summary>
    [Permission("System.Site.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveSiteRequest request, CancellationToken cancellationToken)
    {
        var id = await siteService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑站点。
    /// </summary>
    [Permission("System.Site.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveSiteRequest request, CancellationToken cancellationToken)
    {
        await siteService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改站点状态。
    /// </summary>
    /// <remarks>
    /// 状态：1=启用，0=禁用。
    /// </remarks>
    [Permission("System.Site.UpdateStatus")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateSiteStatusRequest request, CancellationToken cancellationToken)
    {
        await siteService.UpdateStatusAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除站点。
    /// </summary>
    [Permission("System.Site.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await siteService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
