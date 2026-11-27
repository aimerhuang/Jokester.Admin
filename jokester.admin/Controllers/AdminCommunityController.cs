using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Community;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize(Policy = SiteNoticeAdmin.Policy)]
[Route("api/admin/community")]
public sealed class AdminCommunityController(ICommunitySettingService settings) : BaseApiController
{
    [HttpGet("qq-group")]
    [ProducesResponseType(typeof(ApiResponse<AdminQqGroupConfig>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await settings.GetAdminAsync(ct));

    [HttpPut("qq-group")]
    [ProducesResponseType(typeof(ApiResponse<AdminQqGroupConfig>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save([FromBody] SaveQqGroupConfig request, CancellationToken ct) =>
        Success(await settings.SaveAsync(request, ct));
}
