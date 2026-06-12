using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Route("api/blog/comments")]
public sealed class BlogCommentsController(
    IBlogCommentService commentService,
    IBlogCaptchaService captchaService) : BaseApiController
{
    /// <summary>
    /// 获取博客评论验证码。
    /// </summary>
    /// <remarks>
    /// 返回 captchaId 和验证码图片 Base64；提交评论时传回 captchaId 与图片中的答案。验证码校验后会一次性失效。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("captcha")]
    public async Task<IActionResult> Captcha(CancellationToken cancellationToken)
    {
        return Success(await captchaService.CreateAsync(cancellationToken));
    }

    /// <summary>
    /// 公开提交评论。
    /// </summary>
    /// <remarks>
    /// 评论固定写入 blog 站点；新评论默认 status=0 待审核，不会立即出现在公开评论列表。
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("public")]
    public async Task<IActionResult> CreatePublic(
        [FromBody] CreateBlogCommentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await commentService.CreateAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers["User-Agent"].ToString(),
            cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 公开查询已审核评论。
    /// </summary>
    /// <remarks>
    /// 仅返回 status=1 已通过评论；可用 articleId 和 parentId 分页查询评论树。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublicPage(
        [FromQuery] PublicBlogCommentQuery query,
        CancellationToken cancellationToken)
    {
        var result = await commentService.GetPublicPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 后台分页查询评论。
    /// </summary>
    /// <remarks>
    /// 状态：0=待审核，1=已通过，2=已拒绝，3=垃圾评论。
    /// </remarks>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetPage(
        [FromQuery] BlogCommentQuery query,
        CancellationToken cancellationToken)
    {
        var result = await commentService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 审核评论。
    /// </summary>
    /// <remarks>
    /// status 只能提交 1=已通过、2=已拒绝、3=垃圾评论。
    /// </remarks>
    [Authorize]
    [Permission("Blog.Comment.Review")]
    [HttpPut("{id:long}/review")]
    public async Task<IActionResult> Review(
        long id,
        [FromBody] ReviewBlogCommentRequest request,
        CancellationToken cancellationToken)
    {
        await commentService.ReviewAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除评论。
    /// </summary>
    [Authorize]
    [Permission("Blog.Comment.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await commentService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
