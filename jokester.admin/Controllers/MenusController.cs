using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Menus;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
public sealed class MenusController(IMenuService menuService) : BaseApiController
{
    /// <summary>
    /// 查询菜单树。
    /// </summary>
    /// <remarks>
    /// 可按站点 ID 过滤，返回菜单、按钮和接口权限节点的树形结构。
    /// </remarks>
    [Permission("System.Menu.View")]
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree([FromQuery] long? siteId, CancellationToken cancellationToken)
    {
        var result = await menuService.GetTreeAsync(siteId, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询菜单。
    /// </summary>
    [Permission("System.Menu.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] MenuQuery query, CancellationToken cancellationToken)
    {
        var result = await menuService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 新增菜单。
    /// </summary>
    /// <remarks>
    /// menuType：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </remarks>
    [Permission("System.Menu.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveMenuRequest request, CancellationToken cancellationToken)
    {
        var id = await menuService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑菜单。
    /// </summary>
    /// <remarks>
    /// menuType：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </remarks>
    [Permission("System.Menu.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveMenuRequest request, CancellationToken cancellationToken)
    {
        await menuService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除菜单。
    /// </summary>
    [Permission("System.Menu.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await menuService.DeleteAsync(id, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改菜单状态。
    /// </summary>
    /// <remarks>
    /// 状态：1=启用，0=禁用。
    /// </remarks>
    [Permission("System.Menu.UpdateStatus")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateMenuStatusRequest request, CancellationToken cancellationToken)
    {
        await menuService.UpdateStatusAsync(id, request, cancellationToken);
        return Success();
    }
}
