using System.Text.Json;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException ex)
        {
            if (context.RequestAborted.IsCancellationRequested)
            {
                logger.LogInformation("Request was canceled by the client.");

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 499;
                }

                return;
            }

            logger.LogWarning(ex, "Request operation was canceled.");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                context.Response.ContentType = "application/json; charset=utf-8";

                var payload = JsonSerializer.Serialize(ApiResponse.Failure(ErrorCodes.ServerError, "Request timeout"));
                await context.Response.WriteAsync(payload);
            }
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = MapHttpStatusCode(ex.Code);
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = JsonSerializer.Serialize(ApiResponse.Failure(ex.Code, ex.Message));
            await context.Response.WriteAsync(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = JsonSerializer.Serialize(ApiResponse.Failure(ErrorCodes.ServerError, "Server error"));
            await context.Response.WriteAsync(payload);
        }
    }

    private static int MapHttpStatusCode(int code) => code switch
    {
        ErrorCodes.BadRequest => StatusCodes.Status400BadRequest,
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest
    };
}
