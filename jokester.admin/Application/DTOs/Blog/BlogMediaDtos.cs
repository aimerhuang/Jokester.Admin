using jokester.admin.Application.DTOs.Common;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客媒体分页查询参数。
/// </summary>
public sealed class BlogMediaQuery : PageQuery
{
    /// <summary>
    /// 媒体 MIME 类型。
    /// </summary>
    public string? MimeType { get; init; }
}

/// <summary>
/// 博客媒体信息。
/// </summary>
public sealed class BlogMediaDto
{
    /// <summary>
    /// 媒体 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 站点 ID。
    /// </summary>
    public long SiteId { get; init; }

    /// <summary>
    /// 文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// 文件访问地址。
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// MIME 类型。
    /// </summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>
    /// 文件大小，单位字节。
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 图片宽度，单位像素。
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// 图片高度，单位像素。
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// 存储提供方。
    /// </summary>
    public string StorageProvider { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 上传媒体结果。
/// </summary>
public sealed class UploadMediaResponse
{
    /// <summary>
    /// 媒体 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 文件访问地址。
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// 文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// MIME 类型。
    /// </summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>
    /// 文件大小，单位字节。
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 图片宽度，单位像素。
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// 图片高度，单位像素。
    /// </summary>
    public int? Height { get; init; }
}

/// <summary>
/// 上传媒体请求。
/// </summary>
public sealed class UploadMediaRequest
{
    /// <summary>
    /// 媒体文件。
    /// </summary>
    public IFormFile? File { get; init; }
}
