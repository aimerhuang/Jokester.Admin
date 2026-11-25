using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

/// <summary>超级管理员管理生图积分、VIP 优惠和模型访问策略。</summary>
[Authorize]
[Route("api/ai/model-pricing")]
public sealed class AiModelPricingController(AiImagePricingAdminService service) : BaseApiController
{
    /// <summary>获取模型价格矩阵、缺价档位及有效折扣。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PricingOverview>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Success(await service.GetAsync(ct));

    /// <summary>新增价格档并计算 VIP 价。</summary>
    [HttpPost("prices")]
    [ProducesResponseType(typeof(ApiResponse<PricingWriteResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePricingRow request, CancellationToken ct) =>
        Success(await service.CreateAsync(request, ct));

    /// <summary>原子保存批量调价及审计记录。</summary>
    [HttpPut("prices/batch")]
    [ProducesResponseType(typeof(ApiResponse<PricingWriteResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Batch([FromBody] BatchPricingRequest request, CancellationToken ct) =>
        Success(await service.BatchAsync(request, ct));

    /// <summary>预览默认或模型折扣影响的价格档。</summary>
    [HttpPost("discount/preview")]
    [ProducesResponseType(typeof(ApiResponse<DiscountPreview>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview([FromBody] DiscountPreviewRequest request, CancellationToken ct) =>
        Success(await service.PreviewAsync(request, ct));

    /// <summary>确认并应用默认折扣。</summary>
    [HttpPut("discount")]
    [ProducesResponseType(typeof(ApiResponse<PricingWriteResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultDiscount([FromBody] ApplyDiscountRequest request, CancellationToken ct) =>
        Success(await service.ApplyDiscountAsync(null, request, ct));

    /// <summary>应用模型折扣或恢复跟随默认折扣。</summary>
    [HttpPut("models/{modelCode}/discount")]
    [ProducesResponseType(typeof(ApiResponse<PricingWriteResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetModelDiscount(string modelCode, [FromBody] ApplyDiscountRequest request, CancellationToken ct) =>
        Success(await service.ApplyDiscountAsync(modelCode, request, ct));

    /// <summary>设置模型访问策略。</summary>
    [HttpPut("models/{modelCode}/access")]
    [ProducesResponseType(typeof(ApiResponse<PricingWriteResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAccess(string modelCode, [FromBody] ChangeAccessRequest request, CancellationToken ct) =>
        Success(await service.AccessAsync(modelCode, request, ct));

    /// <summary>分页筛选定价与策略变更审计。</summary>
    [HttpGet("audits")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PricingAuditRow>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Audits([FromQuery] PricingAuditQuery query, CancellationToken ct) =>
        Success(await service.AuditsAsync(query, ct));
}
