using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[AllowAnonymous]
[Route("api/integrations/apple/app-store-server-notifications")]
public sealed class AppleIntegrationsController(IAppleIapService appleIapService) : BaseApiController
{
    [HttpPost("v2")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> ReceiveV2(
        [FromBody] AppleServerNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await appleIapService.ReceiveNotificationAsync(request, CancellationToken.None);
        return Success(result);
    }
}
