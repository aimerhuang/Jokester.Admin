using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Roles;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
public sealed class RolesController(IRoleService roleService) : BaseApiController
{
    /// <summary>
    /// 分页查询角色。
    /// </summary>
    [Permission("System.Role.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] RoleQuery query, CancellationToken cancellationToken)
    {
        var result = await roleService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 新增角色。
    /// </summary>
    [Permission("System.Role.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var id = await roleService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑角色。
    /// </summary>
    [Permission("System.Role.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        await roleService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 分配角色菜单和权限。
    /// </summary>
    [Permission("System.Role.AssignMenus")]
    [HttpPut("{id:long}/menus")]
    public async Task<IActionResult> AssignMenus(long id, [FromBody] AssignRoleMenusRequest request, CancellationToken cancellationToken)
    {
        await roleService.AssignMenusAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改角色状态。
    /// </summary>
    /// <remarks>
    /// 状态：1=启用，0=禁用。
    /// </remarks>
    [Permission("System.Role.UpdateStatus")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateRoleStatusRequest request, CancellationToken cancellationToken)
    {
        await roleService.UpdateStatusAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除角色。
    /// </summary>
    [Permission("System.Role.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await roleService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
