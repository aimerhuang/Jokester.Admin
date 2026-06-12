using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Authorization;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/blog/media")]
public sealed class BlogMediaController(IBlogMediaService mediaService, ICurrentUser currentUser) : BaseApiController
{
    /// <summary>
    /// 上传博客媒体文件。
    /// </summary>
    /// <remarks>
    /// 使用 multipart/form-data 上传文件；媒体固定归属 siteCode=blog。
    /// </remarks>
    [Permission("Blog.Media.Upload")]
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] UploadMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            throw new AppException(ErrorCodes.BadRequest, "file is required");
        }

        var uploaderId = currentUser.UserId
            ?? throw new AppException(ErrorCodes.Unauthorized, "Unauthorized");
        var result = await mediaService.UploadAsync(request.File, uploaderId, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询博客媒体。
    /// </summary>
    [Permission("Blog.Media.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] BlogMediaQuery query, CancellationToken cancellationToken)
    {
        var result = await mediaService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 删除博客媒体。
    /// </summary>
    [Permission("Blog.Media.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await mediaService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
