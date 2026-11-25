using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace jokester.admin.Controllers;

/// <summary>记录 VIP 专属生图模型的月卡引导点击。</summary>
[ApiController]
[Authorize]
[Route("api/ai/model-pricing-events")]
public sealed class AiModelPricingEventsController(ISqlSugarClient db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>记录当前被锁定用户从模型引导进入月卡购买页的事件。</summary>
    [HttpPost("vip-guide/{modelCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordGuide(string modelCode, CancellationToken ct)
    {
        if (currentUser.UserId is not long userId) return Unauthorized();
        if (modelCode != AiImageModelConfigService.NormalizeModelCode(modelCode) ||
            !await db.Queryable<AiImageModelPolicyEntity>().AnyAsync(x => x.ModelCode == modelCode && x.AccessLevel == "vip_only", ct))
            return NotFound();
        var eligible = await db.Queryable<SysUserEntity>().AnyAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1 && !x.IsSuperAdmin, ct);
        if (!eligible || await new MembershipEntitlementLedger(db).GetActiveAsync(userId, DateTime.Now, ct) is not null)
            return Conflict();
        // OperationLogMiddleware persists the authenticated user, model path and HTTP outcome.
        return NoContent();
    }
}
