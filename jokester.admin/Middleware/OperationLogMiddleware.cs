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
        var requestBody = await ReadRequestBodyAsync(context.Request);
        var originalResponseBody = context.Response.Body;

        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var responseBody = await ReadResponseBodyAsync(responseBuffer);
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalResponseBody, context.RequestAborted);
            context.Response.Body = originalResponseBody;

            try
            {
                var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                await auditLogWriter.WriteOperationAsync(
                    currentUser.UserId,
                    action?.ControllerName,
                    action?.ActionName,
                    context.Request.Method,
                    $"{context.Request.Path}{context.Request.QueryString}",
                    requestBody,
                    responseBody,
                    (int)stopwatch.ElapsedMilliseconds,
                    context.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write operation log");
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

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
        {
            return null;
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static async Task<string?> ReadResponseBodyAsync(Stream responseBuffer)
    {
        responseBuffer.Position = 0;
        using var reader = new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}
