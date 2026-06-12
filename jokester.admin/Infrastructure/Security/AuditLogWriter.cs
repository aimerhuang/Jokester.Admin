using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Infrastructure.Security;

public sealed class AuditLogWriter(ISqlSugarClient db, IHttpContextAccessor httpContextAccessor) : IAuditLogWriter
{
    public async Task WriteLoginAsync(long? userId, string? userName, bool success, string? errorMessage, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var entity = new SysLoginLogEntity
        {
            UserId = userId,
            UserName = userName,
            Ip = context?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context?.Request.Headers.UserAgent.ToString(),
            LoginStatus = success ? 1 : 0,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.Now
        };

        await db.Insertable(entity).ExecuteCommandAsync();
    }

    public async Task WriteOperationAsync(
        long? userId,
        string? moduleName,
        string? actionName,
        string? requestMethod,
        string? requestUrl,
        string? requestData,
        string? responseData,
        int? executionMs,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var entity = new SysOperationLogEntity
        {
            UserId = userId,
            ModuleName = moduleName,
            ActionName = actionName,
            RequestMethod = requestMethod,
            RequestUrl = requestUrl,
            RequestData = requestData,
            ResponseData = responseData,
            Ip = context?.Connection.RemoteIpAddress?.ToString(),
            ExecutionMs = executionMs,
            CreatedAt = DateTime.Now
        };

        await db.Insertable(entity).ExecuteCommandAsync();
    }
}
