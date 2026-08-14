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

            logger.LogWarning(
                "Request operation was canceled. FailureType={FailureType}, TraceId={TraceId}",
                ex.GetType().Name,
                context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Failure(
                    MachineErrorCodes.ServerError,
                    "Request timeout",
                    context.TraceIdentifier));
            }
        }
        catch (AppException ex)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = MapHttpStatusCode(ex.Code);
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Failure(
                    ex.MachineCode,
                    ex.Message,
                    context.TraceIdentifier,
                    ex.Details));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Unhandled exception. FailureType={FailureType}, TraceId={TraceId}",
                ex.GetType().Name,
                context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Failure(
                    MachineErrorCodes.ServerError,
                    "Server error",
                    context.TraceIdentifier));
            }
        }
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
}
