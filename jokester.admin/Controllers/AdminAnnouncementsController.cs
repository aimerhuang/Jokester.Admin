using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize(Policy = SiteNoticeAdmin.Policy)]
[Route("api/admin/announcements")]
public sealed class AdminAnnouncementsController(IAnnouncementAdminService announcements) : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminAnnouncementListItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AdminAnnouncementQuery query, CancellationToken ct) =>
        Success(await announcements.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AdminAnnouncementDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) => Success(await announcements.DetailAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminAnnouncementDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest request, CancellationToken ct) =>
        Success(await announcements.CreateAsync(request, ct));

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AdminAnnouncementDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAnnouncementRequest request, CancellationToken ct) =>
        Success(await announcements.UpdateAsync(id, request, ct));

    [HttpPut("{id:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<AdminAnnouncementDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatus(long id, [FromBody] AnnouncementStatusRequest request, CancellationToken ct) =>
        Success(await announcements.ChangeStatusAsync(id, request, ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int? revision, CancellationToken ct)
    {
        await announcements.DeleteAsync(id, revision, ct);
        return Success();
    }

    [HttpPost("images")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<AnnouncementCoverUpload>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct) => Success(await announcements.UploadCoverAsync(file, ct));
}
