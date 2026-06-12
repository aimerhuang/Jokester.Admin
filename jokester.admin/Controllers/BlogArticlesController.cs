using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/blog/articles")]
public sealed class BlogArticlesController(IBlogArticleService articleService) : BaseApiController
{
    /// <summary>
    /// 分页查询博客文章。
    /// </summary>
    /// <remarks>
    /// 公开接口；博客文章固定归属 siteCode=blog，可按状态和关键词筛选。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] BlogArticleQuery query, CancellationToken cancellationToken)
    {
        var result = await articleService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 获取博客文章详情。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await articleService.GetByIdAsync(id, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 新增博客文章。
    /// </summary>
    /// <remarks>
    /// 博客文章固定归属 siteCode=blog；可通过 status 设置草稿、发布或隐藏状态。
    /// </remarks>
    [Permission("Blog.Article.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveBlogArticleRequest request, CancellationToken cancellationToken)
    {
        var id = await articleService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑博客文章。
    /// </summary>
    /// <remarks>
    /// 可修改标题、摘要、正文、封面、标签和文章状态。
    /// </remarks>
    [Permission("Blog.Article.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveBlogArticleRequest request, CancellationToken cancellationToken)
    {
        await articleService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除博客文章。
    /// </summary>
    [Permission("Blog.Article.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await articleService.DeleteAsync(id, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 发布博客文章。
    /// </summary>
    [Permission("Blog.Article.Publish")]
    [HttpPost("{id:long}/publish")]
    public async Task<IActionResult> Publish(long id, CancellationToken cancellationToken)
    {
        await articleService.PublishAsync(id, cancellationToken);
        return Success();
    }
}
