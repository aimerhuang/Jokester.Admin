using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiCanvases;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiCanvasService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IOptions<AiCanvasOptions> options) : IAiCanvasService
{
    private const int MaxTitleLength = 100;
    private const string DefaultTitlePrefix = "无限画布";
    private const string UploadedAssetType = "reference";

    public async Task<AiCanvasPageResult> GetPageAsync(AiCanvasQuery query, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        RefAsync<int> total = 0;
        var entities = await db.Queryable<AiCanvasProjectEntity>()
            .Where(x => x.OwnerUserId == userId && !x.IsDeleted)
            .OrderBy(x => x.UpdatedAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Select(x => new AiCanvasProjectEntity
            {
                Id = x.Id,
                CanvasId = x.CanvasId,
                Title = x.Title,
                Revision = x.Revision,
                NodeCount = x.NodeCount,
                ConnectionCount = x.ConnectionCount,
                CoverAssetId = x.CoverAssetId,
                StorageBytes = x.StorageBytes,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new AiCanvasPageResult
        {
            Total = total.Value,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = entities.Select(MapSummary).ToArray(),
            TotalStorageBytes = await GetTotalStorageBytesAsync(userId, cancellationToken)
        };
    }

    public async Task<AiCanvasDetailDto> CreateAsync(CreateAiCanvasRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var liveCount = await db.Queryable<AiCanvasProjectEntity>()
            .Where(x => x.OwnerUserId == userId && !x.IsDeleted)
            .CountAsync(cancellationToken);
        var maxProjects = options.Value.MaxProjectsPerUser;
        if (liveCount >= maxProjects)
        {
            throw new AppException(
                ErrorCodes.UnprocessableEntity,
                MachineErrorCodes.CanvasLimitExceeded,
                $"Canvas maxProjects is {maxProjects}.",
                new { limit = "maxProjects", value = maxProjects });
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"{DefaultTitlePrefix} {liveCount + 1}"
            : NormalizeTitle(request.Title);
        var document = AiCanvasDocumentValidator.CreateEmptyDocument();
        var now = DateTime.UtcNow;
        var entity = new AiCanvasProjectEntity
        {
            CanvasId = CreateCanvasId(now),
            OwnerUserId = userId,
            Title = title,
            SchemaVersion = AiCanvasDocumentValidator.SchemaVersion,
            Revision = 1,
            Document = document,
            DocumentBytes = Encoding.UTF8.GetByteCount(document),
            StorageBytes = Encoding.UTF8.GetByteCount(document),
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
        entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        return MapDetail(entity);
    }

    public async Task<AiCanvasDetailDto> GetAsync(string canvasId, CancellationToken cancellationToken)
    {
        var entity = await FindOwnedAsync(RequireUserId(), canvasId, cancellationToken);
        return MapDetail(entity);
    }

    public async Task<SaveAiCanvasDocumentResponse> SaveDocumentAsync(
        string canvasId,
        SaveAiCanvasDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var entity = await FindOwnedAsync(userId, canvasId, cancellationToken);
        var limits = options.Value;
        var summary = AiCanvasDocumentValidator.Validate(
            request.Document,
            new AiCanvasDocumentLimits(limits.MaxNodesPerProject, limits.MaxConnectionsPerProject, limits.MaxDocumentBytes));

        var assets = await LoadOwnedAssetsAsync(userId, summary.AssetIds, cancellationToken);
        await EnsureOwnedTasksAsync(userId, summary.SourceTaskIds, cancellationToken);

        // 已删除的自有素材仍允许保留在文档中（前端显示“素材已失效”），但不计入体积、不作为封面。
        var storageBytes = summary.DocumentBytes + assets
            .Where(x => !x.IsDeleted && x.AssetType == UploadedAssetType)
            .Sum(x => x.SizeBytes);
        var coverAssetId = summary.CoverAssetId is not null
            && assets.Any(x => x.AssetId == summary.CoverAssetId && !x.IsDeleted)
                ? summary.CoverAssetId
                : null;
        var now = DateTime.UtcNow;
        var nextRevision = request.BaseRevision + 1;

        await db.Ado.BeginTranAsync();
        try
        {
            var affected = await db.Updateable<AiCanvasProjectEntity>()
                .SetColumns(x => new AiCanvasProjectEntity
                {
                    SchemaVersion = AiCanvasDocumentValidator.SchemaVersion,
                    Revision = nextRevision,
                    Document = summary.Json,
                    DocumentBytes = summary.DocumentBytes,
                    NodeCount = summary.NodeCount,
                    ConnectionCount = summary.ConnectionCount,
                    CoverAssetId = coverAssetId,
                    StorageBytes = storageBytes,
                    UpdatedAt = now
                })
                .Where(x => x.Id == entity.Id
                    && x.OwnerUserId == userId
                    && !x.IsDeleted
                    && x.Revision == request.BaseRevision)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                await db.Ado.RollbackTranAsync();
                throw await CreateRevisionConflictAsync(entity.Id, userId, cancellationToken);
            }

            await db.Deleteable<AiCanvasProjectAssetEntity>()
                .Where(x => x.CanvasProjectId == entity.Id)
                .ExecuteCommandAsync(cancellationToken);
            if (summary.AssetIds.Count > 0)
            {
                await db.Insertable(summary.AssetIds.Select(assetId => new AiCanvasProjectAssetEntity
                {
                    CanvasProjectId = entity.Id,
                    AssetId = assetId,
                    OwnerUserId = userId
                }).ToList()).ExecuteCommandAsync(cancellationToken);
            }
            await db.Ado.CommitTranAsync();
        }
        catch (Exception ex) when (ex is not AppException { MachineCode: MachineErrorCodes.CanvasRevisionConflict or MachineErrorCodes.ResourceNotFound })
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        return new SaveAiCanvasDocumentResponse
        {
            Revision = nextRevision,
            UpdatedAt = ApiDateTime.FromUtcStorage(now),
            StorageBytes = storageBytes,
            NodeCount = summary.NodeCount,
            ConnectionCount = summary.ConnectionCount,
            CoverAssetId = coverAssetId,
            CoverUrl = ToCoverUrl(coverAssetId)
        };
    }

    public async Task<AiCanvasSummaryDto> RenameAsync(
        string canvasId,
        RenameAiCanvasRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var entity = await FindOwnedAsync(userId, canvasId, cancellationToken);
        var title = NormalizeTitle(request.Title);
        var now = DateTime.UtcNow;
        await db.Updateable<AiCanvasProjectEntity>()
            .SetColumns(x => new AiCanvasProjectEntity { Title = title, UpdatedAt = now })
            .Where(x => x.Id == entity.Id && x.OwnerUserId == userId && !x.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
        entity.Title = title;
        entity.UpdatedAt = now;
        return MapSummary(entity);
    }

    public async Task DeleteAsync(string canvasId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var entity = await FindOwnedAsync(userId, canvasId, cancellationToken);
        var now = DateTime.UtcNow;
        await db.Updateable<AiCanvasProjectEntity>()
            .SetColumns(x => new AiCanvasProjectEntity { IsDeleted = true, DeletedAt = now, UpdatedAt = now })
            .Where(x => x.Id == entity.Id && x.OwnerUserId == userId && !x.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    public static bool IsCanvasId(string? value) =>
        value is { Length: >= 12 and <= 40 }
        && value.StartsWith("CNV", StringComparison.Ordinal)
        && value.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private async Task<AiCanvasProjectEntity> FindOwnedAsync(long userId, string canvasId, CancellationToken cancellationToken)
    {
        if (!IsCanvasId(canvasId))
        {
            throw HiddenCanvasNotFound();
        }
        return await db.Queryable<AiCanvasProjectEntity>()
            .FirstAsync(x => x.CanvasId == canvasId && x.OwnerUserId == userId && !x.IsDeleted, cancellationToken)
            ?? throw HiddenCanvasNotFound();
    }

    private async Task<AppException> CreateRevisionConflictAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        var currentRevision = await db.Queryable<AiCanvasProjectEntity>()
            .Where(x => x.Id == projectId && x.OwnerUserId == userId && !x.IsDeleted)
            .Select(x => (long?)x.Revision)
            .FirstAsync(cancellationToken);
        return currentRevision is null
            ? HiddenCanvasNotFound()
            : new AppException(
                ErrorCodes.Conflict,
                MachineErrorCodes.CanvasRevisionConflict,
                "Canvas has been saved by another session.",
                new { currentRevision });
    }

    private async Task<IReadOnlyList<MediaAssetEntity>> LoadOwnedAssetsAsync(
        long userId,
        IReadOnlyList<string> assetIds,
        CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }
        var ids = assetIds.ToArray();
        var assets = await db.Queryable<MediaAssetEntity>()
            .Where(x => ids.Contains(x.AssetId) && x.OwnerUserId == userId)
            .ToListAsync(cancellationToken);
        if (assets.Count != ids.Length)
        {
            throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Asset does not exist.");
        }
        return assets;
    }

    private async Task EnsureOwnedTasksAsync(long userId, IReadOnlyList<long> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return;
        }
        var ids = taskIds.ToArray();
        var ownedCount = await db.Queryable<AiImageTaskEntity>()
            .Where(x => ids.Contains(x.Id) && x.UserId == userId)
            .CountAsync(cancellationToken);
        if (ownedCount != ids.Length)
        {
            throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "AI image task does not exist.");
        }
    }

    private async Task<long> GetTotalStorageBytesAsync(long userId, CancellationToken cancellationToken)
    {
        var projects = await db.Queryable<AiCanvasProjectEntity>()
            .Where(x => x.OwnerUserId == userId && !x.IsDeleted)
            .Select(x => new { x.Id, x.DocumentBytes })
            .ToListAsync(cancellationToken);
        if (projects.Count == 0)
        {
            return 0;
        }
        var projectIds = projects.Select(x => x.Id).ToArray();
        var assetIds = await db.Queryable<AiCanvasProjectAssetEntity>()
            .Where(x => projectIds.Contains(x.CanvasProjectId))
            .Select(x => x.AssetId)
            .Distinct()
            .ToListAsync(cancellationToken);
        long assetBytes = 0;
        if (assetIds.Count > 0)
        {
            var ids = assetIds.ToArray();
            assetBytes = await db.Queryable<MediaAssetEntity>()
                .Where(x => ids.Contains(x.AssetId)
                    && x.OwnerUserId == userId
                    && !x.IsDeleted
                    && x.AssetType == UploadedAssetType)
                .SumAsync(x => x.SizeBytes);
        }
        return projects.Sum(x => (long)x.DocumentBytes) + assetBytes;
    }

    private long RequireUserId() =>
        currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "Authentication is required.");

    private static string NormalizeTitle(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length is 0 or > MaxTitleLength || value.Any(char.IsControl))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                $"title must contain 1-{MaxTitleLength} non-control characters.");
        }
        return value;
    }

    private static AiCanvasSummaryDto MapSummary(AiCanvasProjectEntity entity) => new()
    {
        CanvasId = entity.CanvasId,
        Title = entity.Title,
        CoverAssetId = entity.CoverAssetId,
        CoverUrl = ToCoverUrl(entity.CoverAssetId),
        StorageBytes = entity.StorageBytes,
        NodeCount = entity.NodeCount,
        ConnectionCount = entity.ConnectionCount,
        Revision = entity.Revision,
        CreatedAt = ApiDateTime.FromUtcStorage(entity.CreatedAt),
        UpdatedAt = ApiDateTime.FromUtcStorage(entity.UpdatedAt)
    };

    private static AiCanvasDetailDto MapDetail(AiCanvasProjectEntity entity)
    {
        using var document = JsonDocument.Parse(entity.Document);
        return new AiCanvasDetailDto
        {
            CanvasId = entity.CanvasId,
            Title = entity.Title,
            CoverAssetId = entity.CoverAssetId,
            CoverUrl = ToCoverUrl(entity.CoverAssetId),
            StorageBytes = entity.StorageBytes,
            NodeCount = entity.NodeCount,
            ConnectionCount = entity.ConnectionCount,
            Revision = entity.Revision,
            CreatedAt = ApiDateTime.FromUtcStorage(entity.CreatedAt),
            UpdatedAt = ApiDateTime.FromUtcStorage(entity.UpdatedAt),
            SchemaVersion = entity.SchemaVersion,
            Document = document.RootElement.Clone()
        };
    }

    private static string? ToCoverUrl(string? coverAssetId) =>
        coverAssetId is null ? null : $"/api/assets/{coverAssetId}/thumbnail";

    private static string CreateCanvasId(DateTime now) =>
        $"CNV{now:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..35].ToUpperInvariant();

    private static AppException HiddenCanvasNotFound() =>
        new(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Canvas does not exist.");
}
