using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/announcements")]
public sealed class AnnouncementsController(IAnnouncementService announcements) : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AnnouncementFeed>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Success(await announcements.ListAsync(pageIndex, pageSize, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AnnouncementDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) => Success(await announcements.DetailAsync(id, ct));
}
