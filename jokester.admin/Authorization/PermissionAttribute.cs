using Microsoft.AspNetCore.Authorization;

namespace jokester.admin.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class PermissionAttribute(string code) : AuthorizeAttribute
{
    public string Code { get; } = code;
}
