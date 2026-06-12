using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Authorization;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/ai/images")]
public sealed class AiImagesController(
    IAiImageService aiImageService,
    INanoBananaImageService nanoBananaImageService,
    IAiImageModelConfigService modelConfigService) : BaseApiController
{
    /// <summary>
    /// 分页查询 GPT Image2 图片任务
    /// </summary>
    [Permission("AiImage.Page")]
    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] AiImageQuery query, CancellationToken cancellationToken)
    {
        var result = await aiImageService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 获取启用的 AI 图片模型列表
    /// </summary>
    [Permission("AiImage.Generate")]
    [HttpGet("models")]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var result = await modelConfigService.GetEnabledModelsAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 获取 GPT Image2 图片参数选项
    /// </summary>
    [Permission("AiImage.Generate")]
    [HttpGet("parameters")]
    public async Task<IActionResult> GetParameters(CancellationToken cancellationToken)
    {
        var result = await aiImageService.GetParameterOptionsAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 获取 AI 图片积分定价列表
    /// </summary>
    [Permission("AiImage.Generate")]
    [HttpGet("pricing-options")]
    public async Task<IActionResult> GetPricingOptions(CancellationToken cancellationToken)
    {
        var result = await aiImageService.GetPricingOptionsAsync(cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 解析 GPT Image2 图片参数为实际宽高
    /// </summary>
    [Permission("AiImage.Generate")]
    [HttpPost("parameters/resolve")]
    public async Task<IActionResult> ResolveParameters([FromBody] ResolveAiImageParametersRequest request, CancellationToken cancellationToken)
    {
        var result = await aiImageService.ResolveParametersAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 获取 GPT Image2 图片任务详情
    /// </summary>
    [Permission("AiImage.Record.View")]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await aiImageService.GetByIdAsync(id, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 直接生成 GPT Image2 图片
    /// </summary>
    /// <remarks>
    /// 调用配置的 GPT Image2 图片模型并返回 Base64 图片数据。
    /// </remarks>
    [Permission("AiImage.Generate")]
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateAiImageRequest request, CancellationToken cancellationToken)
    {
        var result = await aiImageService.GenerateAsync(request, CancellationToken.None);
        return Success(result);
    }

    /// <summary>
    /// 直接生成 Nano Banana2 图片
    /// </summary>
    /// <remarks>
    /// 不传 imageUrls 时执行文生图；传 imageUrls 时执行图生图。
    /// </remarks>
    [Permission("AiImage.Generate")]
    [HttpPost("nanoBananaImage/generate")]
    public async Task<IActionResult> GenerateNanoBananaImage([FromBody] GenerateNanoBananaImageRequest request, CancellationToken cancellationToken)
    {
        var result = await nanoBananaImageService.GenerateAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 创建 Nano Banana2 图片生成任务
    /// </summary>
    /// <remarks>
    /// 不传 imageUrls 时执行文生图；传 imageUrls 时执行图生图。返回任务 id，前端可轮询 GET /api/ai/images/{id}。
    /// </remarks>
    [Permission("AiImage.Generate")]
    [HttpPost("nanoBananaImage")]
    public async Task<IActionResult> CreateNanoBananaImage([FromBody] CreateNanoBananaImageTaskRequest request, CancellationToken cancellationToken)
    {
        var id = await nanoBananaImageService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 上传 AI 图片引用文件
    /// </summary>
    /// <remarks>
    /// 使用 multipart/form-data 上传图片，登录用户即可调用，返回服务器访问路径。
    /// </remarks>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadAiImageRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            throw new AppException(ErrorCodes.BadRequest, "file is required");
        }

        var result = await aiImageService.UploadAsync(request.File, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 创建 GPT Image2 图片生成任务
    /// </summary>
    [Permission("AiImage.Generate")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAiImageTaskRequest request, CancellationToken cancellationToken)
    {
        var id = await aiImageService.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    /// <summary>
    /// 收藏或取消收藏 GPT Image2 结果图片
    /// </summary>
    [HttpPost("{id:long}/favorite")]
    public async Task<IActionResult> SetFavorite(long id, [FromBody] FavoriteAiImageRequest request, CancellationToken cancellationToken)
    {
        await aiImageService.SetFavoriteAsync(id, request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 删除 GPT Image2 图片任务
    /// </summary>
    [Permission("AiImage.Record.Delete")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await aiImageService.DeleteAsync(id, cancellationToken);
        return Success();
    }
}
