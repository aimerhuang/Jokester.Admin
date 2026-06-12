using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Logs;

/// <summary>
/// 登录日志分页查询参数。
/// </summary>
public sealed class LoginLogQuery : PageQuery
{
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// 登录状态：1=成功，0=失败。
    /// </summary>
    public int? LoginStatus { get; init; }

    /// <summary>
    /// 登录 IP。
    /// </summary>
    public string? Ip { get; init; }
}

/// <summary>
/// 操作日志分页查询参数。
/// </summary>
public sealed class OperationLogQuery : PageQuery
{
    /// <summary>
    /// 模块名称。
    /// </summary>
    public string? ModuleName { get; init; }

    /// <summary>
    /// 操作名称。
    /// </summary>
    public string? ActionName { get; init; }

    /// <summary>
    /// 请求方法。
    /// </summary>
    public string? RequestMethod { get; init; }

    /// <summary>
    /// 操作用户 ID。
    /// </summary>
    public long? UserId { get; init; }
}

/// <summary>
/// 登录日志。
/// </summary>
public sealed class LoginLogDto
{
    /// <summary>
    /// 日志 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long? UserId { get; init; }

    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 登录 IP。
    /// </summary>
    public string? Ip { get; init; }

    /// <summary>
    /// User-Agent。
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// 登录状态：1=成功，0=失败。
    /// </summary>
    public int LoginStatus { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 操作日志。
/// </summary>
public sealed class OperationLogDto
{
    /// <summary>
    /// 日志 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long? UserId { get; init; }

    /// <summary>
    /// 模块名称。
    /// </summary>
    public string? ModuleName { get; init; }

    /// <summary>
    /// 操作名称。
    /// </summary>
    public string? ActionName { get; init; }

    /// <summary>
    /// 请求方法。
    /// </summary>
    public string? RequestMethod { get; init; }

    /// <summary>
    /// 请求地址。
    /// </summary>
    public string? RequestUrl { get; init; }

    /// <summary>
    /// 请求数据。
    /// </summary>
    public string? RequestData { get; init; }

    /// <summary>
    /// 响应数据。
    /// </summary>
    public string? ResponseData { get; init; }

    /// <summary>
    /// 操作 IP。
    /// </summary>
    public string? Ip { get; init; }

    /// <summary>
    /// 执行耗时，单位毫秒。
    /// </summary>
    public int? ExecutionMs { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 批量删除日志请求。
/// </summary>
public sealed class DeleteLogsRequest
{
    /// <summary>
    /// 日志 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> Ids { get; init; } = Array.Empty<long>();
}
