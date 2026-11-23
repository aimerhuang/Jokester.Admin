using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/admin/users")]
public sealed class AdminUsersController(IUserAdminService users) : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminUserListItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AdminUserQuery query, CancellationToken ct) =>
        Success(await users.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) =>
        Success(await users.DetailAsync(id, ct));

    [HttpGet("{id:long}/point-summary")]
    [ProducesResponseType(typeof(ApiResponse<AdminPointSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(long id, [FromQuery] AdminPointRange query, CancellationToken ct) =>
        Success(await users.SummaryAsync(id, query, ct));

    [HttpGet("{id:long}/point-details")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PointDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Details(long id, [FromQuery] AdminPointQuery query, CancellationToken ct) =>
        Success(await users.DetailsAsync(id, query, ct));

    [HttpPut("{id:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<AdminStatusResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatus(long id, [FromBody] AdminStatusRequest request, CancellationToken ct) =>
        Success(await users.ChangeStatusAsync(id, request, ct));

    [HttpGet("{id:long}/status-audits")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminStatusAudit>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Audits(long id, [FromQuery] AdminPageQuery query, CancellationToken ct) =>
        Success(await users.AuditsAsync(id, query, ct));
}
