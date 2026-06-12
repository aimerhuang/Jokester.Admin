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

    public static ApiResponse Success(string message = "success") => new()
    {
        Code = ErrorCodes.Success,
        Message = message
    };

    public static ApiResponse Failure(int code, string message) => new()
    {
        Code = code,
        Message = message
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

    public static ApiResponse<T> Success(T data, string message = "success") => new()
    {
        Code = ErrorCodes.Success,
        Message = message,
        Data = data
    };
}
