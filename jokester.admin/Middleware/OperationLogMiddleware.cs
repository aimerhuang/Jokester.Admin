using System.Diagnostics;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
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
        Exception? failure = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
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
                    BuildOutcome(context, failure),
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

    private static string BuildOutcome(HttpContext context, Exception? failure)
    {
        var statusCode = failure switch
        {
            AppException ex => MapHttpStatusCode(ex.Code),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => 499,
            OperationCanceledException => StatusCodes.Status408RequestTimeout,
            not null => StatusCodes.Status500InternalServerError,
            _ => context.Response.StatusCode
        };
        var errorCode = failure switch
        {
            AppException ex => ex.MachineCode,
            OperationCanceledException => MachineErrorCodes.ServerError,
            not null => MachineErrorCodes.ServerError,
            _ when statusCode == StatusCodes.Status429TooManyRequests => MachineErrorCodes.RateLimited,
            _ when statusCode >= StatusCodes.Status500InternalServerError => MachineErrorCodes.ServerError,
            _ => null
        };

        return JsonSerializer.Serialize(new
        {
            statusCode,
            succeeded = statusCode < StatusCodes.Status400BadRequest,
            errorCode
        });
    }

    private static int MapHttpStatusCode(int code) => code switch
    {
        ErrorCodes.BadRequest => StatusCodes.Status400BadRequest,
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorCodes.UnprocessableEntity or ErrorCodes.AiPromptRejected => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorCodes.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.ServerError => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest
    };

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
