using System.Diagnostics;
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
                var interfaceName = BuildInterfaceName(context, action);
                await auditLogWriter.WriteOperationAsync(
                    currentUser.UserId,
                    NormalizeName(action?.ControllerName),
                    interfaceName,
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

        return context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/swagger");
    }

    private static string BuildInterfaceName(HttpContext context, ControllerActionDescriptor? action)
    {
        var routePattern = context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? $"/{routeEndpoint.RoutePattern.RawText?.TrimStart('/')}"
            : context.Request.Path.Value;

        var controllerAction = action is null
            ? null
            : $"{NormalizeName(action.ControllerName)}.{NormalizeName(action.ActionName)}";

        var name = string.IsNullOrWhiteSpace(controllerAction)
            ? $"{context.Request.Method} {routePattern}"
            : $"{context.Request.Method} {routePattern} => {controllerAction}";

        return Truncate(name, 100);
    }

    private static string? NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..maxLength]}...[truncated {value.Length - maxLength} chars]";
    }
}
