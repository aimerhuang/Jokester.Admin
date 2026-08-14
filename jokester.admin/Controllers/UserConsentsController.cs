using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Legal;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/users/me/consents")]
public sealed class UserConsentsController(IUserConsentService consentService) : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserConsentsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await consentService.GetCurrentUserConsentsAsync(cancellationToken);
        return Success(result);
    }

    [HttpPut("ai-processing")]
    [ProducesResponseType(typeof(ApiResponse<ConsentRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAiProcessing(
        [FromBody] UpdateAiProcessingConsentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await consentService.UpdateAiProcessingAsync(request, cancellationToken);
        return Success(result);
    }
}
