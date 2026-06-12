namespace jokester.admin.Application.Abstractions;

public interface IAuditLogWriter
{
    Task WriteLoginAsync(long? userId, string? userName, bool success, string? errorMessage, CancellationToken cancellationToken);

    Task WriteOperationAsync(
        long? userId,
        string? moduleName,
        string? actionName,
        string? requestMethod,
        string? requestUrl,
        string? requestData,
        string? responseData,
        int? executionMs,
        CancellationToken cancellationToken);
}
