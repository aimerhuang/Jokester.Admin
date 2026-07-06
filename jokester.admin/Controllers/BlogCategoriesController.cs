using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/blog/categories")]
public sealed class BlogCategoriesController(IBlogCategoryService categoryService) : BaseApiController
{
    /// <summary>
    /// 获取博客分类列表。
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        return Success(await categoryService.GetListAsync(cancellationToken));
    }

    /// <summary>
    /// 新增博客分类。
    /// </summary>
    [Permission("Blog.Category.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveBlogCategoryRequest request, CancellationToken cancellationToken)
    {
        var id = await categoryService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 编辑博客分类。
    /// </summary>
    [Permission("Blog.Category.Update")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveBlogCategoryRequest request, CancellationToken cancellationToken)
    {
        await categoryService.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除博客分类。
    /// </summary>
    [Permission("Blog.Category.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await categoryService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
