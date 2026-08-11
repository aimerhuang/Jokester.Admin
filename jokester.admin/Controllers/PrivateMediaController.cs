using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using System.Text.Json;

namespace jokester.admin.Controllers;

[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/media/ai")]
public sealed class PrivateMediaController(ICurrentUser currentUser, ISqlSugarClient db, IAiMediaPathResolver mediaPathResolver) : ControllerBase
{
    [HttpGet("{*path}")]
    public async Task<IActionResult> Download(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')) return NotFound();
        var userId = currentUser.UserId;
        if (!userId.HasValue) return Unauthorized();

        var url = "/api/media/ai/" + path;
        var ownPrefix = userId.Value + "/";
        var permitted = currentUser.IsSuperAdmin || path.StartsWith(ownPrefix, StringComparison.Ordinal);
        if (!permitted)
        {
            var tasks = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.UserId == userId.Value && !x.IsDeleted)
                .Select(x => new { x.ResultUrls, x.ReferenceImageUrls, x.MaskImageUrl })
                .ToListAsync(cancellationToken);
            permitted = tasks.Any(x => ContainsExactUrl(x.ResultUrls, url)
                || ContainsExactUrl(x.ReferenceImageUrls, url)
                || string.Equals(x.MaskImageUrl, url, StringComparison.Ordinal));
        }
        if (!permitted) return NotFound();

        string file;
        try
        {
            file = mediaPathResolver.ResolveFilePath(path);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        if (!System.IO.File.Exists(file)) return NotFound();
        var type = Path.GetExtension(file).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "image/png" };
        return PhysicalFile(file, type, enableRangeProcessing: false);
    }

    private static bool ContainsExactUrl(string? json, string url)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var urls = JsonSerializer.Deserialize<IReadOnlyList<string>>(json);
            return urls?.Contains(url, StringComparer.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
