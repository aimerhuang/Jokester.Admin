using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Sites;

/// <summary>
/// 站点信息。
/// </summary>
public sealed class SiteDto
{
    /// <summary>
    /// 站点 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 站点名称。
    /// </summary>
    public string SiteName { get; init; } = string.Empty;

    /// <summary>
    /// 站点编码。
    /// </summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>
    /// 站点域名。
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 站点描述。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 排序值。
    /// </summary>
    public int Sort { get; init; }
}

/// <summary>
/// 站点分页查询参数。
/// </summary>
public sealed class SiteQuery : PageQuery
{
    /// <summary>
    /// 关键词，匹配站点名称或编码。
    /// </summary>
    public string? Keyword { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int? Status { get; init; }
}

/// <summary>
/// 保存站点请求。
/// </summary>
public sealed class SaveSiteRequest
{
    /// <summary>
    /// 站点名称。
    /// </summary>
    public string SiteName { get; init; } = string.Empty;

    /// <summary>
    /// 站点编码。
    /// </summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>
    /// 站点域名。
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; } = 1;

    /// <summary>
    /// 站点描述。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 排序值。
    /// </summary>
    public int Sort { get; init; }
}

/// <summary>
/// 修改站点状态请求。
/// </summary>
public sealed class UpdateSiteStatusRequest
{
    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }
}
