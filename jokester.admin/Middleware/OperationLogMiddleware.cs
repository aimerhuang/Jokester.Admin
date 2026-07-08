using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using jokester.admin.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace jokester.admin.Middleware;

public sealed class OperationLogMiddleware(RequestDelegate next, ILogger<OperationLogMiddleware> logger)
{
    private const int MaxLoggedBodyLength = 16 * 1024;
    private static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwordHash",
        "salt",
        "token",
        "accessToken",
        "refreshToken",
        "apiKey",
        "secret",
        "authorization",
        "base64",
        "dataUrl"
    };

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
                var interfaceName = BuildInterfaceName(context, action);
                await auditLogWriter.WriteOperationAsync(
                    currentUser.UserId,
                    NormalizeName(action?.ControllerName),
                    interfaceName,
                    context.Request.Method,
                    $"{context.Request.Path}{context.Request.QueryString}",
                    BuildRequestLog(context.Request, requestBody),
                    BuildResponseLog(context.Response, responseBody),
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
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        return context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/swagger");
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
        {
            return null;
        }

        if (!CanReadTextBody(request.ContentType))
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

    private static async Task<string?> ReadResponseBodyAsync(MemoryStream responseBuffer)
    {
        if (responseBuffer.Length == 0)
        {
            return null;
        }

        responseBuffer.Position = 0;
        using var reader = new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(body) ? null : body;
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

    private static string BuildRequestLog(HttpRequest request, string? body)
    {
        var payload = new JsonObject
        {
            ["method"] = request.Method,
            ["path"] = request.Path.Value,
            ["queryString"] = request.QueryString.HasValue ? request.QueryString.Value : null,
            ["contentType"] = request.ContentType,
            ["body"] = BuildLoggedBody(body, request.ContentType, request.ContentLength)
        };

        return payload.ToJsonString();
    }

    private static string BuildResponseLog(HttpResponse response, string? body)
    {
        var payload = new JsonObject
        {
            ["statusCode"] = response.StatusCode,
            ["contentType"] = response.ContentType,
            ["body"] = BuildLoggedBody(body, response.ContentType, response.ContentLength)
        };

        return payload.ToJsonString();
    }

    private static JsonNode? BuildLoggedBody(string? body, string? contentType, long? contentLength)
    {
        if (!CanReadTextBody(contentType))
        {
            return JsonValue.Create(contentLength.HasValue
                ? $"[non-text body omitted, {contentLength.Value} bytes]"
                : "[non-text body omitted]");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        if (IsJsonContentType(contentType))
        {
            try
            {
                var json = Redact(JsonNode.Parse(body));
                if (json is null)
                {
                    return null;
                }

                var serialized = json.ToJsonString();
                return serialized.Length > MaxLoggedBodyLength
                    ? JsonValue.Create(Truncate(serialized, MaxLoggedBodyLength))
                    : json;
            }
            catch (JsonException)
            {
                return JsonValue.Create(Truncate(body, MaxLoggedBodyLength));
            }
        }

        return JsonValue.Create(Truncate(body, MaxLoggedBodyLength));
    }

    private static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            var redacted = new JsonObject();
            foreach (var property in jsonObject)
            {
                redacted[property.Key] = SensitiveFieldNames.Contains(property.Key)
                    ? JsonValue.Create("[redacted]")
                    : Redact(property.Value);
            }

            return redacted;
        }

        if (node is JsonArray jsonArray)
        {
            var redacted = new JsonArray();
            foreach (var item in jsonArray)
            {
                redacted.Add(Redact(item));
            }

            return redacted;
        }

        return node?.DeepClone();
    }

    private static bool CanReadTextBody(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
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
