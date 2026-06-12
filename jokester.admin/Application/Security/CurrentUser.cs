using System.Security.Claims;
using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Security;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public long? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(value, out var userId) ? userId : null;
        }
    }

    public string? UserName => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public bool IsSuperAdmin => string.Equals(
        httpContextAccessor.HttpContext?.User.FindFirstValue("is_super_admin"),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
