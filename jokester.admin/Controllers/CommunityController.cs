using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Community;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/community")]
public sealed class CommunityController(ICommunitySettingService settings) : BaseApiController
{
    [HttpGet("qq-group")]
    [ProducesResponseType(typeof(ApiResponse<QqGroupConfig>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await settings.GetAsync(ct));
}
