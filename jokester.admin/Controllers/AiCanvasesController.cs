using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiCanvases;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/ai/canvases")]
public sealed class AiCanvasesController(IAiCanvasService canvasService) : BaseApiController
{
    /// <summary>
    /// 分页查询当前用户的无限画布项目（不含文档内容）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AiCanvasPageResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPage([FromQuery] AiCanvasQuery query, CancellationToken cancellationToken)
    {
        return Success(await canvasService.GetPageAsync(query, cancellationToken));
    }

    /// <summary>
    /// 创建空白无限画布
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AiCanvasDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAiCanvasRequest request, CancellationToken cancellationToken)
    {
        return Success(await canvasService.CreateAsync(request, cancellationToken));
    }

    /// <summary>
    /// 获取无限画布详情与文档
    /// </summary>
    [HttpGet("{canvasId}")]
    [ProducesResponseType(typeof(ApiResponse<AiCanvasDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string canvasId, CancellationToken cancellationToken)
    {
        return Success(await canvasService.GetAsync(canvasId, cancellationToken));
    }

    /// <summary>
    /// 保存无限画布文档
    /// </summary>
    /// <remarks>
    /// 使用 baseRevision 做乐观并发控制；修订号不一致时返回 409 CANVAS_REVISION_CONFLICT，details.currentRevision 为服务端当前修订号。
    /// </remarks>
    [HttpPut("{canvasId}/document")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<SaveAiCanvasDocumentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDocument(
        string canvasId,
        [FromBody] SaveAiCanvasDocumentRequest request,
        CancellationToken cancellationToken)
    {
        return Success(await canvasService.SaveDocumentAsync(canvasId, request, cancellationToken));
    }

    /// <summary>
    /// 重命名无限画布
    /// </summary>
    [HttpPatch("{canvasId}")]
    [ProducesResponseType(typeof(ApiResponse<AiCanvasSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(
        string canvasId,
        [FromBody] RenameAiCanvasRequest request,
        CancellationToken cancellationToken)
    {
        return Success(await canvasService.RenameAsync(canvasId, request, cancellationToken));
    }

    /// <summary>
    /// 删除无限画布（软删除，不删除素材与生图任务）
    /// </summary>
    [HttpDelete("{canvasId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(string canvasId, CancellationToken cancellationToken)
    {
        await canvasService.DeleteAsync(canvasId, cancellationToken);
        return Success();
    }
}
