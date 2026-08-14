namespace jokester.admin.Common;

/// <summary>
/// 通用接口响应。
/// </summary>
public class ApiResponse
{
    /// <summary>
    /// 业务状态码，0 表示成功。
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; init; } = "success";

    /// <summary>
    /// 服务端请求追踪 ID。
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    public static ApiResponse Success(string requestId, string message = "success") => new()
    {
        Code = ErrorCodes.Success,
        Message = message,
        RequestId = requestId
    };
}

/// <summary>
/// 带数据的通用接口响应。
/// </summary>
public class ApiResponse<T> : ApiResponse
{
    /// <summary>
    /// 响应数据。
    /// </summary>
    public T? Data { get; init; }

    public static ApiResponse<T> Success(T data, string requestId, string message = "success") => new()
    {
        Code = ErrorCodes.Success,
        Message = message,
        RequestId = requestId,
        Data = data
    };
}

/// <summary>
/// 通用失败响应。机器可读错误码与成功响应的数值 code 分开建模。
/// </summary>
public sealed class ApiErrorResponse
{
    public string Code { get; init; } = MachineErrorCodes.ServerError;

    public string Message { get; init; } = string.Empty;

    public string RequestId { get; init; } = string.Empty;

    public object? Details { get; init; }

    public static ApiErrorResponse Failure(
        string code,
        string message,
        string requestId,
        object? details = null) => new()
        {
            Code = code,
            Message = message,
            RequestId = requestId,
            Details = details
        };
}
