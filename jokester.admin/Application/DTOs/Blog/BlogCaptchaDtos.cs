namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客评论验证码。
/// </summary>
public sealed class BlogCaptchaDto
{
    /// <summary>
    /// 验证码 ID。
    /// </summary>
    public string CaptchaId { get; init; } = string.Empty;

    /// <summary>
    /// Base64 编码的验证码图片。
    /// </summary>
    public string ImageBase64 { get; init; } = string.Empty;

    /// <summary>
    /// 图片 MIME 类型。
    /// </summary>
    public string MimeType { get; init; } = "image/svg+xml";

    /// <summary>
    /// 有效期，单位秒。
    /// </summary>
    public int ExpiresInSeconds { get; init; }
}
