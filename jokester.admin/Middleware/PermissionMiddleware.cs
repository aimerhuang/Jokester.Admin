using jokester.admin.Application.Abstractions;
using jokester.admin.Authorization;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Middleware;

public sealed class PermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IPermissionService permissionService)
    {
        var requiredPermissions = context.GetEndpoint()?.Metadata.GetOrderedMetadata<PermissionAttribute>() ?? [];
        if (requiredPermissions.Count == 0)
        {
            await next(context);
            return;
        }

        if (!currentUser.UserId.HasValue)
        {
            throw new AppException(ErrorCodes.Unauthorized, "未登录");
        }

        if (currentUser.IsSuperAdmin)
        {
            await next(context);
            return;
        }

        var permissions = await permissionService.GetPermissionsAsync(currentUser.UserId.Value, false, context.RequestAborted);
        var missing = requiredPermissions.FirstOrDefault(x => !permissions.Contains(x.Code, StringComparer.OrdinalIgnoreCase));
        if (missing is not null)
        {
            throw new AppException(ErrorCodes.Forbidden, $"缺少权限: {missing.Code}");
        }

        await next(context);
    }
}
