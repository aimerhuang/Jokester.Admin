using jokester.admin.Application.Abstractions;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/admin/prompt-sync")]
public sealed class PromptLibrarySyncController(IPromptLibrarySyncAdminService syncAdminService) : BaseApiController
{
    [Permission("PromptLibrary.Sync.View")]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await syncAdminService.GetStatusAsync(cancellationToken);
        return Success(result);
    }

    [Permission("PromptLibrary.Sync.Run")]
    [HttpPost("run")]
    public IActionResult Run()
    {
        var result = syncAdminService.QueueRun();
        return Success(
            result,
            result.Accepted
                ? "Prompt synchronization queued."
                : "Prompt synchronization is unavailable while another synchronization or snapshot switch is active.");
    }

    [Permission("PromptLibrary.Sync.Switch")]
    [HttpPost("snapshots/{snapshotId:long}/activate")]
    public async Task<IActionResult> ActivateSnapshot(long snapshotId, CancellationToken cancellationToken)
    {
        var result = await syncAdminService.SwitchSnapshotAsync(snapshotId, cancellationToken);
        return Success(result, result.Changed ? "Prompt snapshot activated." : "Prompt snapshot is already active.");
    }
}
