using System.Text.Json;
using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.AiCanvases;

public sealed class AiCanvasQuery : PageQuery
{
}

public sealed class CreateAiCanvasRequest
{
    /// <summary>
    /// 画布名称，留空时由后端生成“无限画布 N”。
    /// </summary>
    public string? Title { get; init; }
}

public sealed class RenameAiCanvasRequest
{
    public string Title { get; init; } = string.Empty;
}

public sealed class SaveAiCanvasDocumentRequest
{
    /// <summary>
    /// 客户端编辑所基于的修订号，与服务端当前修订号不一致时返回 409。
    /// </summary>
    public long BaseRevision { get; init; }

    /// <summary>
    /// 画布文档（schemaVersion=1），结构见 docs/ai-infinite-canvas-prd.md。
    /// </summary>
    public JsonElement Document { get; init; }
}

public class AiCanvasSummaryDto
{
    public string CanvasId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? CoverAssetId { get; init; }

    public string? CoverUrl { get; init; }

    public long StorageBytes { get; init; }

    public int NodeCount { get; init; }

    public int ConnectionCount { get; init; }

    public long Revision { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}

public sealed class AiCanvasDetailDto : AiCanvasSummaryDto
{
    public int SchemaVersion { get; init; }

    public JsonElement Document { get; init; }
}

public sealed class AiCanvasPageResult
{
    public long Total { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public IReadOnlyCollection<AiCanvasSummaryDto> Items { get; init; } = [];

    public bool HasMore => (long)PageIndex * PageSize < Total;

    /// <summary>
    /// 当前用户全部未删除画布的素材总体积（上传素材按 Asset 去重）。
    /// </summary>
    public long TotalStorageBytes { get; init; }
}

public sealed class SaveAiCanvasDocumentResponse
{
    public long Revision { get; init; }

    public DateTime UpdatedAt { get; init; }

    public long StorageBytes { get; init; }

    public int NodeCount { get; init; }

    public int ConnectionCount { get; init; }

    public string? CoverAssetId { get; init; }

    public string? CoverUrl { get; init; }
}

public sealed class RegisterAiResultAssetRequest
{
    public long TaskId { get; init; }

    public int ResultOrdinal { get; init; }
}
