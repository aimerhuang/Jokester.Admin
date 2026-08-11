using System.Diagnostics;
using System.Text;
using jokester.admin.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace jokester.admin.Middleware;

public sealed class OperationLogMiddleware(RequestDelegate next, ILogger<OperationLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IAuditLogWriter auditLogWriter)
    {
        if (!ShouldLog(context))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            try
            {
                var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                await auditLogWriter.WriteOperationAsync(
                    currentUser.UserId,
                    action?.ControllerName,
                    action?.ActionName,
                    context.Request.Method,
                    context.Request.Path,
                    null,
                    null,
                    (int)stopwatch.ElapsedMilliseconds,
                    context.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Failed to write operation log. FailureType={FailureType}, TraceId={TraceId}",
                    ex.GetType().Name,
                    context.TraceIdentifier);
            }
        }
    }

    private static bool ShouldLog(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        return !context.Request.Path.StartsWithSegments("/swagger");
    }

}
