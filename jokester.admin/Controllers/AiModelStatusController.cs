using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

/// <summary>
/// Public model status for every signed-in user. No menu permission is required.
/// </summary>
[Authorize]
[Route("api/ai/images/model-status")]
public sealed class AiModelStatusController(IAiImageModelStatusService statusService) : BaseApiController
{
    /// <summary>
    /// 获取各模型近期可用性、最快/最慢成功耗时和最近请求成败序列（只读快照，约 30 秒刷新）。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelStatusResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await statusService.GetAsync(cancellationToken);
        return Success(result);
    }
}
