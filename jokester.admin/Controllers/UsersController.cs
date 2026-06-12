using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Authorization;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
public sealed class UsersController(IUserService userService) : BaseApiController
{
    /// <summary>
    /// 分页查询用户。
    /// </summary>
    [Permission("System.User.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var result = await userService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询用户积分明细。
    /// </summary>
    [Permission("System.User.View")]
    [HttpGet("{id:long}/point-details")]
    public async Task<IActionResult> GetPointDetails(long id, [FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var result = await userService.GetPointDetailsAsync(id, query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 查询用户授权菜单树。
    /// </summary>
    /// <remarks>
    /// 可按站点 ID 过滤，返回菜单、按钮和接口权限节点的树形结构，并标记当前用户已拥有的节点。
    /// </remarks>
    [Permission("System.User.Authorize")]
    [HttpGet("{id:long}/menus/tree")]
    public async Task<IActionResult> GetPermissionTree(long id, [FromQuery] long? siteId, CancellationToken cancellationToken)
    {
        var result = await userService.GetPermissionTreeAsync(id, siteId, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 新增用户。
    /// </summary>
    [Permission("System.User.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveUserRequest request, CancellationToken cancellationToken)
    {
        var id = await userService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑用户基础信息。
    /// </summary>
    [Permission("System.User.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserInfoRequest request, CancellationToken cancellationToken)
    {
        await userService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 分配用户菜单权限。
    /// </summary>
    /// <remarks>
    /// 通过用户专属授权角色保存菜单、按钮和接口权限；不会覆盖用户已有业务角色。
    /// </remarks>
    [Permission("System.User.Authorize")]
    [HttpPut("{id:long}/menus")]
    public async Task<IActionResult> AssignMenus(long id, [FromBody] AssignUserMenusRequest request, CancellationToken cancellationToken)
    {
        await userService.AssignMenusAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改用户昵称。
    /// </summary>
    [Permission("System.User.Update")]
    [HttpPut("{id:long}/nickname")]
    public async Task<IActionResult> UpdateNickName(long id, [FromBody] UpdateUserNickNameRequest request, CancellationToken cancellationToken)
    {
        await userService.UpdateNickNameAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改用户密码。
    /// </summary>
    [Permission("System.User.Update")]
    [HttpPut("{id:long}/password")]
    public async Task<IActionResult> UpdatePassword(long id, [FromBody] UpdateUserPasswordRequest request, CancellationToken cancellationToken)
    {
        await userService.UpdatePasswordAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 上传用户头像。
    /// </summary>
    /// <remarks>
    /// 使用 multipart/form-data 上传头像文件，返回头像访问地址。
    /// </remarks>
    [Permission("System.User.Update")]
    [HttpPost("{id:long}/avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar(long id, [FromForm] UploadUserAvatarRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            throw new AppException(ErrorCodes.BadRequest, "file is required");
        }

        var url = await userService.UploadAvatarAsync(id, request.File, cancellationToken);
        return Success(new { url });
    }

    /// <summary>
    /// 修改用户个性签名。
    /// </summary>
    [Permission("System.User.Update")]
    [HttpPut("{id:long}/signature")]
    public async Task<IActionResult> UpdateSignature(long id, [FromBody] UpdateUserSignatureRequest request, CancellationToken cancellationToken)
    {
        await userService.UpdateSignatureAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 修改用户状态。
    /// </summary>
    /// <remarks>
    /// 状态：1=启用，0=禁用。
    /// </remarks>
    [Permission("System.User.UpdateStatus")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        await userService.UpdateStatusAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除用户。
    /// </summary>
    [Permission("System.User.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await userService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
