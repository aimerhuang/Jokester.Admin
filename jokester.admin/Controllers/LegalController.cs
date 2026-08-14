using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Legal;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[AllowAnonymous]
[Route("api/legal/documents")]
public sealed class LegalController(ILegalDocumentService legalDocumentService) : BaseApiController
{
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<CurrentLegalDocumentsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] string? platform,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        var result = await legalDocumentService.GetCurrentAsync(platform, locale, cancellationToken);
        return Success(result);
    }
}
