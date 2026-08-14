using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/assets")]
public sealed class AssetsController(IMediaAssetService mediaAssetService, ICurrentUser currentUser) : BaseApiController
{
    [HttpGet("{assetId}/content")]
    public Task<IActionResult> Content(string assetId, CancellationToken cancellationToken) =>
        Download(assetId, thumbnail: false, cancellationToken);

    [HttpGet("{assetId}/thumbnail")]
    public Task<IActionResult> Thumbnail(string assetId, CancellationToken cancellationToken) =>
        Download(assetId, thumbnail: true, cancellationToken);

    [HttpDelete("{assetId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(string assetId, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "Authentication is required.");
        await mediaAssetService.DeleteOwnedAsync(assetId, currentUser.UserId.Value, cancellationToken);
        return Success();
    }

    private async Task<IActionResult> Download(string assetId, bool thumbnail, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "Authentication is required.");
        var content = await mediaAssetService.GetContentAsync(
            assetId,
            currentUser.UserId.Value,
            currentUser.IsSuperAdmin,
            thumbnail,
            cancellationToken);
        if (content is null)
            throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Asset does not exist.");
        Response.Headers.CacheControl = "private, max-age=300";
        return PhysicalFile(content.FullPath, content.MimeType, enableRangeProcessing: false);
    }
}
