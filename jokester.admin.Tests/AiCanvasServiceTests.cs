using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiCanvases;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiCanvasServiceTests : IDisposable
{
    private const string OwnUpload = "AST20261103000000000000000000000001";
    private const string OwnGenerated = "AST20261103000000000000000000000002";
    private const string OtherUsersAsset = "AST20261103000000000000000000000003";
    private const string OwnDeletedUpload = "AST20261103000000000000000000000004";

    private readonly SqlSugarClient _db;
    private long? _currentUserId = 1;

    public AiCanvasServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        CreateSchema(_db);
        InsertAsset(OwnUpload, 1, "reference", 1000);
        InsertAsset(OwnGenerated, 1, "generated", 5000, sourceTaskId: 10);
        InsertAsset(OtherUsersAsset, 2, "reference", 700);
        InsertAsset(OwnDeletedUpload, 1, "reference", 300, isDeleted: true);
        _db.Ado.ExecuteCommand("INSERT INTO ai_image_task (id, user_id, status, is_deleted) VALUES (10, 1, 1, 0), (11, 2, 1, 0)");
    }

    [Fact]
    public async Task Create_ReturnsEmptyDocumentWithDefaultTitle()
    {
        var service = CreateService();

        var first = await service.CreateAsync(new CreateAiCanvasRequest(), default);
        var second = await service.CreateAsync(new CreateAiCanvasRequest { Title = "  我的画布  " }, default);

        Assert.StartsWith("CNV", first.CanvasId);
        Assert.Equal("无限画布 1", first.Title);
        Assert.Equal("我的画布", second.Title);
        Assert.Equal(1, first.Revision);
        Assert.Equal(JsonValueKind.Array, first.Document.GetProperty("nodes").ValueKind);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);
    }

    [Fact]
    public async Task Create_RejectsWhenProjectLimitReached()
    {
        var service = CreateService(new AiCanvasOptions { MaxProjectsPerUser = 1 });
        await service.CreateAsync(new CreateAiCanvasRequest(), default);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(new CreateAiCanvasRequest(), default));

        Assert.Equal(MachineErrorCodes.CanvasLimitExceeded, exception.MachineCode);
    }

    [Fact]
    public async Task OtherUsersCanvas_IsHiddenForEveryOperation()
    {
        var canvas = await CreateService().CreateAsync(new CreateAiCanvasRequest(), default);
        _currentUserId = 2;
        var service = CreateService();

        var errors = new[]
        {
            await Assert.ThrowsAsync<AppException>(() => service.GetAsync(canvas.CanvasId, default)),
            await Assert.ThrowsAsync<AppException>(() => service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(1, DocumentWith()), default)),
            await Assert.ThrowsAsync<AppException>(() => service.RenameAsync(canvas.CanvasId, new RenameAiCanvasRequest { Title = "x" }, default)),
            await Assert.ThrowsAsync<AppException>(() => service.DeleteAsync(canvas.CanvasId, default)),
            await Assert.ThrowsAsync<AppException>(() => service.GetAsync("CNV-not-valid", default))
        };

        Assert.All(errors, error => Assert.Equal(MachineErrorCodes.ResourceNotFound, error.MachineCode));
        Assert.Empty((await service.GetPageAsync(new AiCanvasQuery(), default)).Items);
    }

    [Fact]
    public async Task Save_IncrementsRevisionRebuildsAssetIndexAndComputesStorage()
    {
        var service = CreateService();
        var canvas = await service.CreateAsync(new CreateAiCanvasRequest(), default);
        var document = DocumentWith(
            Image("a", OwnUpload),
            Image("b", OwnGenerated, sourceTaskId: 10),
            Image("c", OwnDeletedUpload));

        var saved = await service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(1, document), default);

        Assert.Equal(2, saved.Revision);
        Assert.Equal(3, saved.NodeCount);
        Assert.Equal(OwnGenerated, saved.CoverAssetId);
        Assert.Equal($"/api/assets/{OwnGenerated}/thumbnail", saved.CoverUrl);
        var stored = _db.Queryable<AiCanvasProjectEntity>().Single();
        // 只计入未删除的上传素材，生成结果不重复计入画布体积。
        Assert.Equal(stored.DocumentBytes + 1000, saved.StorageBytes);
        Assert.Equal(
            [OwnUpload, OwnGenerated, OwnDeletedUpload],
            _db.Queryable<AiCanvasProjectAssetEntity>().Select(x => x.AssetId).ToList().Order());

        var reloaded = await service.GetAsync(canvas.CanvasId, default);
        Assert.Equal(2, reloaded.Revision);
        Assert.Equal(3, reloaded.Document.GetProperty("nodes").GetArrayLength());

        await service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(2, DocumentWith(Image("a", OwnUpload))), default);
        Assert.Equal([OwnUpload], _db.Queryable<AiCanvasProjectAssetEntity>().Select(x => x.AssetId).ToList());
    }

    [Fact]
    public async Task Save_WithStaleRevisionReturnsConflictAndKeepsDocument()
    {
        var service = CreateService();
        var canvas = await service.CreateAsync(new CreateAiCanvasRequest(), default);
        await service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(1, DocumentWith(Image("a", OwnUpload))), default);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(1, DocumentWith()), default));

        Assert.Equal(ErrorCodes.Conflict, exception.Code);
        Assert.Equal(MachineErrorCodes.CanvasRevisionConflict, exception.MachineCode);
        Assert.Equal(2L, exception.Details!.GetType().GetProperty("currentRevision")!.GetValue(exception.Details));
        var stored = _db.Queryable<AiCanvasProjectEntity>().Single();
        Assert.Equal(2, stored.Revision);
        Assert.Equal(1, stored.NodeCount);
        Assert.Single(_db.Queryable<AiCanvasProjectAssetEntity>().ToList());
    }

    [Theory]
    [InlineData(OtherUsersAsset, null)]
    [InlineData("AST20261103000000000000000000000999", null)]
    [InlineData(OwnUpload, 11L)]
    [InlineData(OwnUpload, 999L)]
    public async Task Save_RejectsAssetsOrTasksNotOwnedByCurrentUser(string assetId, long? sourceTaskId)
    {
        var service = CreateService();
        var canvas = await service.CreateAsync(new CreateAiCanvasRequest(), default);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.SaveDocumentAsync(canvas.CanvasId, SaveRequest(1, DocumentWith(Image("a", assetId, sourceTaskId))), default));

        Assert.Equal(MachineErrorCodes.ResourceNotFound, exception.MachineCode);
        Assert.Equal(1, _db.Queryable<AiCanvasProjectEntity>().Single().Revision);
    }

    [Fact]
    public async Task Page_ReturnsSummariesAndDeduplicatedTotalStorage()
    {
        var service = CreateService();
        var first = await service.CreateAsync(new CreateAiCanvasRequest { Title = "first" }, default);
        var second = await service.CreateAsync(new CreateAiCanvasRequest { Title = "second" }, default);
        var deleted = await service.CreateAsync(new CreateAiCanvasRequest { Title = "deleted" }, default);
        await service.SaveDocumentAsync(first.CanvasId, SaveRequest(1, DocumentWith(Image("a", OwnUpload))), default);
        await service.SaveDocumentAsync(second.CanvasId, SaveRequest(1, DocumentWith(Image("a", OwnUpload))), default);
        await service.DeleteAsync(deleted.CanvasId, default);

        var page = await service.GetPageAsync(new AiCanvasQuery(), default);

        Assert.Equal(2, page.Total);
        Assert.Equal(["second", "first"], page.Items.Select(x => x.Title));
        var documentBytes = _db.Queryable<AiCanvasProjectEntity>().Where(x => !x.IsDeleted).ToList().Sum(x => (long)x.DocumentBytes);
        Assert.Equal(documentBytes + 1000, page.TotalStorageBytes);
    }

    [Fact]
    public async Task RenameAndDelete_UpdateCanvas()
    {
        var service = CreateService();
        var canvas = await service.CreateAsync(new CreateAiCanvasRequest(), default);

        var renamed = await service.RenameAsync(canvas.CanvasId, new RenameAiCanvasRequest { Title = " 新名字 " }, default);
        await Assert.ThrowsAsync<AppException>(() =>
            service.RenameAsync(canvas.CanvasId, new RenameAiCanvasRequest { Title = new string('x', 101) }, default));
        await service.DeleteAsync(canvas.CanvasId, default);

        Assert.Equal("新名字", renamed.Title);
        Assert.True(_db.Queryable<AiCanvasProjectEntity>().Single().IsDeleted);
        await Assert.ThrowsAsync<AppException>(() => service.GetAsync(canvas.CanvasId, default));
    }

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        _currentUserId = null;

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            CreateService().GetPageAsync(new AiCanvasQuery(), default));

        Assert.Equal(ErrorCodes.Unauthorized, exception.Code);
    }

    public void Dispose() => _db.Dispose();

    private AiCanvasService CreateService(AiCanvasOptions? options = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(() => _currentUserId);
        return new AiCanvasService(_db, currentUser.Object, Options.Create(options ?? new AiCanvasOptions()));
    }

    private static SaveAiCanvasDocumentRequest SaveRequest(long baseRevision, string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return new SaveAiCanvasDocumentRequest { BaseRevision = baseRevision, Document = parsed.RootElement.Clone() };
    }

    private static string DocumentWith(params string[] nodes) =>
        $$$"""{"schemaVersion":1,"nodes":[{{{string.Join(',', nodes)}}}],"connections":[]}""";

    private static string Image(string id, string assetId, long? sourceTaskId = null) =>
        sourceTaskId is null
            ? $$$"""{"id":"{{{id}}}","type":"image","x":0,"y":0,"width":100,"height":100,"data":{"assetId":"{{{assetId}}}"}}"""
            : $$$"""{"id":"{{{id}}}","type":"image","x":0,"y":0,"width":100,"height":100,"data":{"assetId":"{{{assetId}}}","sourceTaskId":{{{sourceTaskId}}},"sourceResultOrdinal":0}}""";

    private void InsertAsset(string assetId, long ownerUserId, string assetType, long sizeBytes, long? sourceTaskId = null, bool isDeleted = false)
    {
        _db.Insertable(new MediaAssetEntity
        {
            AssetId = assetId,
            OwnerUserId = ownerUserId,
            AssetType = assetType,
            StorageKey = $"{ownerUserId}/assets/{assetId}.png",
            ThumbnailKey = $"{ownerUserId}/assets/{assetId}_thumb.webp",
            MimeType = "image/png",
            Width = 1,
            Height = 1,
            SizeBytes = sizeBytes,
            Sha256 = new string('A', 64),
            MetadataStripped = true,
            SourceTaskId = sourceTaskId,
            SourceResultOrdinal = sourceTaskId is null ? null : 0,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted
        }).ExecuteCommand();
    }

    internal static void CreateSchema(ISqlSugarClient db) => db.Ado.ExecuteCommand("""
        CREATE TABLE ai_canvas_project (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            canvas_id TEXT NOT NULL UNIQUE,
            owner_user_id INTEGER NOT NULL,
            title TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            revision INTEGER NOT NULL,
            document TEXT NOT NULL,
            document_bytes INTEGER NOT NULL,
            node_count INTEGER NOT NULL DEFAULT 0,
            connection_count INTEGER NOT NULL DEFAULT 0,
            cover_asset_id TEXT NULL,
            storage_bytes INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            deleted_at TEXT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE ai_canvas_project_asset (
            canvas_project_id INTEGER NOT NULL,
            asset_id TEXT NOT NULL,
            owner_user_id INTEGER NOT NULL,
            PRIMARY KEY (canvas_project_id, asset_id)
        );
        CREATE TABLE media_asset (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_id TEXT NOT NULL UNIQUE,
            owner_user_id INTEGER NOT NULL,
            asset_type TEXT NOT NULL,
            storage_key TEXT NOT NULL UNIQUE,
            thumbnail_key TEXT NULL,
            mime_type TEXT NOT NULL,
            width INTEGER NOT NULL,
            height INTEGER NOT NULL,
            size_bytes INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            metadata_stripped INTEGER NOT NULL,
            source_task_id INTEGER NULL,
            source_result_ordinal INTEGER NULL,
            created_at TEXT NOT NULL,
            deleted_at TEXT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            UNIQUE (source_task_id, source_result_ordinal)
        );
        CREATE TABLE ai_image_task (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            status INTEGER NOT NULL DEFAULT 0,
            result_urls TEXT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE ai_image_task_result (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_id INTEGER NOT NULL,
            result_ordinal INTEGER NOT NULL,
            url TEXT NOT NULL,
            width INTEGER NOT NULL,
            height INTEGER NOT NULL,
            size TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            is_quarantined INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        """);
}
