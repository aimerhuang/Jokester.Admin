using System.Text.Json;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Tests;

public sealed class AiCanvasDocumentValidatorTests
{
    private const string UploadedAsset = "AST20261103000000000000000000000001";
    private const string GeneratedAsset = "AST20261103000000000000000000000002";
    private const string NewerGeneratedAsset = "AST20261103000000000000000000000003";

    private static readonly AiCanvasDocumentLimits Limits = new(MaxNodes: 10, MaxConnections: 10, MaxDocumentBytes: 64 * 1024);

    [Fact]
    public void ValidDocument_ReturnsReferencesCountsAndLatestGeneratedCover()
    {
        var summary = AiCanvasDocumentValidator.Validate(Parse(ValidDocument()), Limits);

        Assert.Equal(4, summary.NodeCount);
        Assert.Equal(2, summary.ConnectionCount);
        Assert.Equal([UploadedAsset, GeneratedAsset, NewerGeneratedAsset], summary.AssetIds);
        Assert.Equal([10L, 20L], summary.SourceTaskIds.Order());
        Assert.Equal(NewerGeneratedAsset, summary.CoverAssetId);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(summary.Json), summary.DocumentBytes);
    }

    [Fact]
    public void DocumentWithoutGeneratedImages_UsesFirstImageAsCover()
    {
        var summary = AiCanvasDocumentValidator.Validate(Parse($$$"""
            {"schemaVersion":1,"nodes":[
              {"id":"t","type":"text","x":0,"y":0,"width":10,"height":10,"data":{"content":"note"}},
              {"id":"a","type":"image","x":0,"y":0,"width":10,"height":10,"data":{"assetId":"{{{UploadedAsset}}}"}}
            ],"connections":[]}
            """), Limits);

        Assert.Equal(UploadedAsset, summary.CoverAssetId);
        Assert.Empty(summary.SourceTaskIds);
    }

    [Fact]
    public void EmptyDocument_IsValid()
    {
        var summary = AiCanvasDocumentValidator.Validate(Parse(AiCanvasDocumentValidator.CreateEmptyDocument()), Limits);

        Assert.Equal(0, summary.NodeCount);
        Assert.Null(summary.CoverAssetId);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"nodes":[],"connections":[],"extra":1}""")]
    [InlineData("""{"schemaVersion":2,"nodes":[],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[]}""")]
    [InlineData("""{"schemaVersion":1,"background":"stars","nodes":[],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"video","x":0,"y":0,"width":1,"height":1,"data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"https://evil.example/a.png"}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"AST20261103000000000000000000000001","url":"https://evil.example/a.png"}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"AST20261103000000000000000000000001","sourceResultOrdinal":0}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a b","type":"text","x":0,"y":0,"width":1,"height":1,"data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"text","x":0,"y":0,"width":0,"height":1,"data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"text","x":0,"y":0,"width":1,"height":1,"groupId":"g 1","data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"text","x":0,"y":0,"width":1,"height":1,"groupId":7,"data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"a","type":"text","x":0,"y":0,"width":1,"height":1,"data":{}},{"id":"a","type":"text","x":0,"y":0,"width":1,"height":1,"data":{}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"g","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{"inputOrder":["missing"]}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"g","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{"sizeMode":"huge"}}],"connections":[]}""")]
    [InlineData("""{"schemaVersion":1,"nodes":[{"id":"g","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{"pendingIdempotencyKey":"short"}}],"connections":[]}""")]
    public void InvalidStructure_IsRejectedAsValidationError(string json)
    {
        var exception = Assert.Throws<AppException>(() => AiCanvasDocumentValidator.Validate(Parse(json), Limits));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Equal(MachineErrorCodes.ValidationError, exception.MachineCode);
    }

    [Theory]
    [InlineData("""[{"id":"e1","from":"a","to":"missing"}]""")]
    [InlineData("""[{"id":"e1","from":"a","to":"b"},{"id":"e2","from":"g1","to":"b"}]""")]
    [InlineData("""[{"id":"e1","from":"a","to":"b"},{"id":"e2","from":"b","to":"a"}]""")]
    [InlineData("""[{"id":"e1","from":"t","to":"g1"}]""")]
    [InlineData("""[{"id":"e1","from":"a","to":"t"}]""")]
    [InlineData("""[{"id":"e1","from":"g1","to":"g1"}]""")]
    [InlineData("""[{"id":"e1","from":"a","to":"g1"},{"id":"e2","from":"a","to":"g1"}]""")]
    [InlineData("""[{"id":"e1","from":"g1","to":"g2"},{"id":"e2","from":"g2","to":"g1"}]""")]
    public void InvalidConnections_AreRejected(string connections)
    {
        var exception = Assert.Throws<AppException>(() =>
            AiCanvasDocumentValidator.Validate(Parse(ConnectionDocument(connections)), Limits));

        Assert.Equal(MachineErrorCodes.ValidationError, exception.MachineCode);
    }

    // 切图、裁剪等派生图片以单条连线指回来源节点。
    [Theory]
    [InlineData("""[{"id":"e1","from":"a","to":"b"}]""")]
    [InlineData("""[{"id":"e1","from":"g1","to":"a"},{"id":"e2","from":"g1","to":"b"}]""")]
    [InlineData("""[{"id":"e1","from":"a","to":"b"},{"id":"e2","from":"b","to":"g1"}]""")]
    public void DerivedImageConnections_AreAccepted(string connections)
    {
        var result = AiCanvasDocumentValidator.Validate(Parse(ConnectionDocument(connections)), Limits);

        Assert.True(result.ConnectionCount > 0);
    }

    [Fact]
    public void GroupedNodes_AreAccepted()
    {
        var json = $$$"""
            {"schemaVersion":1,"nodes":[
              {"id":"a","type":"image","x":0,"y":0,"width":1,"height":1,"groupId":"grp_1","data":{"assetId":"{{{UploadedAsset}}}"}},
              {"id":"t","type":"text","x":0,"y":0,"width":1,"height":1,"groupId":"grp_1","data":{}},
              {"id":"g1","type":"generate","x":0,"y":0,"width":1,"height":1,"groupId":null,"data":{}}
            ],"connections":[]}
            """;

        var result = AiCanvasDocumentValidator.Validate(Parse(json), Limits);

        Assert.Equal(3, result.NodeCount);
    }

    private static string ConnectionDocument(string connections) => $$$"""
        {"schemaVersion":1,"nodes":[
          {"id":"a","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"{{{UploadedAsset}}}"}},
          {"id":"b","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"{{{UploadedAsset}}}"}},
          {"id":"t","type":"text","x":0,"y":0,"width":1,"height":1,"data":{}},
          {"id":"g1","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{}},
          {"id":"g2","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{}}
        ],"connections":{{{connections}}}}
        """;

    [Fact]
    public void TooManyNodes_IsRejectedAsLimitExceeded()
    {
        var nodes = string.Join(',', Enumerable.Range(0, Limits.MaxNodes + 1)
            .Select(i => $$$"""{"id":"n{{{i}}}","type":"text","x":0,"y":0,"width":1,"height":1,"data":{}}"""));

        var exception = Assert.Throws<AppException>(() =>
            AiCanvasDocumentValidator.Validate(Parse($$$"""{"schemaVersion":1,"nodes":[{{{nodes}}}],"connections":[]}"""), Limits));

        Assert.Equal(ErrorCodes.UnprocessableEntity, exception.Code);
        Assert.Equal(MachineErrorCodes.CanvasLimitExceeded, exception.MachineCode);
    }

    [Fact]
    public void TooManyGenerateInputs_IsRejectedAsLimitExceeded()
    {
        var count = AiCanvasDocumentValidator.MaxGenerateInputs + 1;
        var images = string.Join(',', Enumerable.Range(0, count)
            .Select(i => $$$"""{"id":"i{{{i}}}","type":"image","x":0,"y":0,"width":1,"height":1,"data":{"assetId":"{{{UploadedAsset}}}"}}"""));
        var connections = string.Join(',', Enumerable.Range(0, count)
            .Select(i => $$$"""{"id":"e{{{i}}}","from":"i{{{i}}}","to":"g"}"""));
        var json = $$$"""
            {"schemaVersion":1,"nodes":[{{{images}}},{"id":"g","type":"generate","x":0,"y":0,"width":1,"height":1,"data":{}}],
             "connections":[{{{connections}}}]}
            """;

        var exception = Assert.Throws<AppException>(() =>
            AiCanvasDocumentValidator.Validate(Parse(json), Limits with { MaxNodes = 20 }));

        Assert.Equal(MachineErrorCodes.CanvasLimitExceeded, exception.MachineCode);
    }

    [Fact]
    public void OversizedDocument_IsRejected()
    {
        var content = new string('x', 4000);
        var json = $$$"""{"schemaVersion":1,"nodes":[{"id":"t","type":"text","x":0,"y":0,"width":1,"height":1,"data":{"content":"{{{content}}}"}}],"connections":[]}""";

        var exception = Assert.Throws<AppException>(() =>
            AiCanvasDocumentValidator.Validate(Parse(json), Limits with { MaxDocumentBytes = 1024 }));

        Assert.Equal(MachineErrorCodes.CanvasDocumentTooLarge, exception.MachineCode);
    }

    [Fact]
    public void FreeTextMayContainUrlsBecauseNoImageFieldAcceptsThem()
    {
        var summary = AiCanvasDocumentValidator.Validate(Parse("""
            {"schemaVersion":1,"nodes":[
              {"id":"t","type":"text","x":0,"y":0,"width":1,"height":1,"title":"see https://example.test","data":{"content":"https://example.test"}}
            ],"connections":[]}
            """), Limits);

        Assert.Empty(summary.AssetIds);
    }

    private static string ValidDocument() => $$$"""
        {
          "schemaVersion": 1,
          "viewport": { "x": -120.5, "y": 40, "zoom": 0.73 },
          "background": "lines",
          "nodes": [
            { "id": "img_1", "type": "image", "x": 320, "y": 200, "width": 276, "height": 206, "title": "参考图",
              "data": { "assetId": "{{{UploadedAsset}}}" } },
            { "id": "img_2", "type": "image", "x": 320, "y": 500, "width": 276, "height": 206, "title": null,
              "data": { "assetId": "{{{GeneratedAsset}}}", "sourceTaskId": 10, "sourceResultOrdinal": 0 } },
            { "id": "gen_1", "type": "generate", "x": 815, "y": 235, "width": 276, "height": 155, "title": "新建生图",
              "data": {
                "prompt": "使用图片1的构图",
                "modelCode": "gpt-image-2",
                "catalogVersion": "gpt-image-2@2026-08-21.1",
                "sizeMode": "explicit",
                "resolutionCode": "2k",
                "qualityCode": "low",
                "aspectRatioCode": "16:9",
                "imageCount": 1,
                "inputOrder": ["img_1", "img_2"],
                "pendingIdempotencyKey": null,
                "runs": [ { "idempotencyKey": "canvas-run-0001", "taskIds": [20], "createdAt": "2026-11-03T08:00:00Z" } ],
                "primaryResult": { "taskId": 20, "resultOrdinal": 0, "assetId": "{{{NewerGeneratedAsset}}}" }
              } },
            { "id": "note", "type": "text", "x": 0, "y": 0, "width": 200, "height": 80,
              "data": { "content": "备注", "fontSize": 14 } }
          ],
          "connections": [
            { "id": "e1", "from": "img_1", "to": "gen_1" },
            { "id": "e2", "from": "img_2", "to": "gen_1" }
          ]
        }
        """;

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
