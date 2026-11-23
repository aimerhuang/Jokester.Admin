using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

/// <summary>
/// 超级管理员维护 AI 生图模型路由（API 地址、Key、路径、启停）。Key 只写不读。
/// </summary>
[Authorize]
[Route("api/ai/model-routes")]
public sealed class AiModelRoutesController(IAiImageModelRouteAdminService routeAdminService) : BaseApiController
{
    /// <summary>
    /// 按模型分组的路由列表。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AiImageModelRouteGroupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] AiImageModelRouteQuery query, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.GetListAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 路由变更记录。
    /// </summary>
    [HttpGet("audits")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AiImageModelRouteAuditDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudits([FromQuery] AiImageModelRouteAuditQuery query, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.GetAuditsAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 路由详情。
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelRouteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.GetAsync(id, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 为已有模型新增路由。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelRouteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAiImageModelRouteRequest request, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.CreateAsync(request, cancellationToken);
        return Success(result, "路由已创建");
    }

    /// <summary>
    /// 编辑路由；apiKey 留空表示不修改。保存后对新的 Provider 调用立即生效。
    /// </summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelRouteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAiImageModelRouteRequest request, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.UpdateAsync(id, request, cancellationToken);
        return Success(result, "路由已保存");
    }

    /// <summary>
    /// 启用或禁用路由。
    /// </summary>
    [HttpPut("{id:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelRouteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateAiImageModelRouteStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.UpdateStatusAsync(id, request, cancellationToken);
        return Success(result, request.Status == 1 ? "路由已启用" : "路由已禁用");
    }

    /// <summary>
    /// 轻量连通性探测（GET {baseUrl}/models），不出图、不扣积分、不计入模型状态。
    /// </summary>
    [HttpPost("{id:long}/test")]
    [ProducesResponseType(typeof(ApiResponse<AiImageModelRouteProbeResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(long id, CancellationToken cancellationToken)
    {
        var result = await routeAdminService.ProbeAsync(id, cancellationToken);
        return Success(result);
    }
}
