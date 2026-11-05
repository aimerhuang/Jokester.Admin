using System.Text;
using System.Text.Json;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Security;

public sealed record AiCanvasDocumentLimits(int MaxNodes, int MaxConnections, int MaxDocumentBytes);

public sealed record AiCanvasDocumentSummary(
    string Json,
    int DocumentBytes,
    int NodeCount,
    int ConnectionCount,
    IReadOnlyList<string> AssetIds,
    IReadOnlyList<long> SourceTaskIds,
    string? CoverAssetId);

/// <summary>
/// 画布文档白名单校验。文档中不存在任何图片 URL 字段，图片只能以当前用户的 assetId 表达。
/// </summary>
public static class AiCanvasDocumentValidator
{
    public const int SchemaVersion = 1;
    public const int MaxGenerateInputs = 6;

    private const int MaxTitleLength = 200;
    private const int MaxTextContentLength = 5000;
    private const int MaxPromptLength = 4000;
    private const int MaxRunsPerNode = 20;
    private const int MaxTaskIdsPerRun = 10;
    private const int MaxResultOrdinal = 99;
    private const int MaxImageCount = 10;
    private const double MaxCoordinate = 10_000_000;
    private const double MaxNodeSize = 20_000;

    private static readonly HashSet<string> RootKeys = ["schemaVersion", "viewport", "background", "nodes", "connections"];
    private static readonly HashSet<string> ViewportKeys = ["x", "y", "zoom"];
    private static readonly HashSet<string> NodeKeys = ["id", "type", "x", "y", "width", "height", "title", "groupId", "data"];
    private static readonly HashSet<string> ImageDataKeys = ["assetId", "sourceTaskId", "sourceResultOrdinal"];
    private static readonly HashSet<string> TextDataKeys = ["content", "fontSize"];
    private static readonly HashSet<string> GenerateDataKeys =
    [
        "prompt", "modelCode", "catalogVersion", "sizeMode", "resolutionCode", "qualityCode",
        "aspectRatioCode", "imageCount", "inputOrder", "pendingIdempotencyKey", "runs", "primaryResult"
    ];
    private static readonly HashSet<string> RunKeys = ["idempotencyKey", "taskIds", "createdAt"];
    private static readonly HashSet<string> PrimaryResultKeys = ["taskId", "resultOrdinal", "assetId"];
    private static readonly HashSet<string> ConnectionKeys = ["id", "from", "to"];
    private static readonly HashSet<string> Backgrounds = ["lines", "dots", "blank"];
    private static readonly HashSet<string> SizeModes = ["explicit", "auto"];

    public static string CreateEmptyDocument() =>
        """{"schemaVersion":1,"viewport":{"x":0,"y":0,"zoom":1},"background":"lines","nodes":[],"connections":[]}""";

    public static AiCanvasDocumentSummary Validate(JsonElement document, AiCanvasDocumentLimits limits)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("document must be a JSON object.");
        }

        var json = document.GetRawText();
        var documentBytes = Encoding.UTF8.GetByteCount(json);
        if (documentBytes > limits.MaxDocumentBytes)
        {
            throw new AppException(
                ErrorCodes.UnprocessableEntity,
                MachineErrorCodes.CanvasDocumentTooLarge,
                $"Canvas document must not exceed {limits.MaxDocumentBytes} bytes.",
                new { limit = "maxDocumentBytes", value = limits.MaxDocumentBytes });
        }

        var root = ReadObject(document, "document", RootKeys);
        var schemaVersion = RequireInt(root, "schemaVersion", "document", 1, int.MaxValue);
        if (schemaVersion != SchemaVersion)
        {
            throw Invalid($"Unsupported schemaVersion {schemaVersion}.");
        }
        if (root.TryGetValue("viewport", out var viewport) && viewport.ValueKind != JsonValueKind.Null)
        {
            var viewportFields = ReadObject(viewport, "viewport", ViewportKeys);
            RequireNumber(viewportFields, "x", "viewport", -MaxCoordinate, MaxCoordinate);
            RequireNumber(viewportFields, "y", "viewport", -MaxCoordinate, MaxCoordinate);
            RequireNumber(viewportFields, "zoom", "viewport", 0.05, 10);
        }
        var background = OptionalString(root, "background", "document", 10);
        if (background is not null && !Backgrounds.Contains(background))
        {
            throw Invalid("document.background is invalid.");
        }

        var nodes = RequireArray(root, "nodes", "document");
        if (nodes.Count > limits.MaxNodes)
        {
            throw LimitExceeded("maxNodes", limits.MaxNodes);
        }
        var connections = RequireArray(root, "connections", "document");
        if (connections.Count > limits.MaxConnections)
        {
            throw LimitExceeded("maxConnections", limits.MaxConnections);
        }

        var nodeTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var assetIds = new List<string>();
        var sourceTaskIds = new List<long>();
        var inputOrders = new List<(string NodeId, IReadOnlyList<string> Inputs)>();
        string? firstImageAssetId = null;
        (long TaskId, string AssetId)? latestGenerated = null;

        for (var index = 0; index < nodes.Count; index++)
        {
            var path = $"nodes[{index}]";
            var node = ReadObject(nodes[index], path, NodeKeys);
            var id = RequireIdentifier(node, "id", path);
            var type = RequireString(node, "type", path, 20);
            if (!nodeTypes.TryAdd(id, type))
            {
                throw Invalid($"{path}.id is duplicated.");
            }
            RequireNumber(node, "x", path, -MaxCoordinate, MaxCoordinate);
            RequireNumber(node, "y", path, -MaxCoordinate, MaxCoordinate);
            RequireNumber(node, "width", path, 1, MaxNodeSize);
            RequireNumber(node, "height", path, 1, MaxNodeSize);
            OptionalString(node, "title", path, MaxTitleLength);
            // 共享同一 groupId 的节点组成一个组（绑组/解组），组本身不单独存储。
            var groupId = OptionalString(node, "groupId", path, 40);
            if (groupId is not null && !IsIdentifier(groupId))
            {
                throw Invalid($"{path}.groupId is invalid.");
            }
            if (!node.TryGetValue("data", out var dataElement))
            {
                throw Invalid($"{path}.data is required.");
            }

            var dataPath = path + ".data";
            switch (type)
            {
                case "image":
                {
                    var data = ReadObject(dataElement, dataPath, ImageDataKeys);
                    var assetId = RequireAssetId(data, "assetId", dataPath);
                    assetIds.Add(assetId);
                    firstImageAssetId ??= assetId;
                    var sourceTaskId = OptionalLong(data, "sourceTaskId", dataPath, 1, long.MaxValue);
                    var ordinal = OptionalLong(data, "sourceResultOrdinal", dataPath, 0, MaxResultOrdinal);
                    if (ordinal is not null && sourceTaskId is null)
                    {
                        throw Invalid($"{dataPath}.sourceResultOrdinal requires sourceTaskId.");
                    }
                    if (sourceTaskId is not null)
                    {
                        sourceTaskIds.Add(sourceTaskId.Value);
                        latestGenerated = PickLatest(latestGenerated, sourceTaskId.Value, assetId);
                    }
                    break;
                }
                case "text":
                {
                    var data = ReadObject(dataElement, dataPath, TextDataKeys);
                    OptionalString(data, "content", dataPath, MaxTextContentLength);
                    if (data.ContainsKey("fontSize") && data["fontSize"].ValueKind != JsonValueKind.Null)
                    {
                        RequireNumber(data, "fontSize", dataPath, 6, 400);
                    }
                    break;
                }
                case "generate":
                {
                    var data = ReadObject(dataElement, dataPath, GenerateDataKeys);
                    OptionalString(data, "prompt", dataPath, MaxPromptLength);
                    OptionalCode(data, "modelCode", dataPath, 100);
                    OptionalCode(data, "catalogVersion", dataPath, 100);
                    var sizeMode = OptionalString(data, "sizeMode", dataPath, 16);
                    if (sizeMode is not null && !SizeModes.Contains(sizeMode))
                    {
                        throw Invalid($"{dataPath}.sizeMode is invalid.");
                    }
                    OptionalCode(data, "resolutionCode", dataPath, 50);
                    OptionalCode(data, "qualityCode", dataPath, 50);
                    OptionalCode(data, "aspectRatioCode", dataPath, 50);
                    OptionalLong(data, "imageCount", dataPath, 1, MaxImageCount);
                    OptionalIdempotencyKey(data, "pendingIdempotencyKey", dataPath);
                    inputOrders.Add((id, ReadInputOrder(data, dataPath)));
                    ReadRuns(data, dataPath);
                    if (data.TryGetValue("primaryResult", out var primary) && primary.ValueKind != JsonValueKind.Null)
                    {
                        var primaryPath = dataPath + ".primaryResult";
                        var primaryFields = ReadObject(primary, primaryPath, PrimaryResultKeys);
                        var taskId = RequireLong(primaryFields, "taskId", primaryPath, 1, long.MaxValue);
                        RequireLong(primaryFields, "resultOrdinal", primaryPath, 0, MaxResultOrdinal);
                        sourceTaskIds.Add(taskId);
                        if (primaryFields.TryGetValue("assetId", out var primaryAsset)
                            && primaryAsset.ValueKind != JsonValueKind.Null)
                        {
                            var assetId = RequireAssetId(primaryFields, "assetId", primaryPath);
                            assetIds.Add(assetId);
                            latestGenerated = PickLatest(latestGenerated, taskId, assetId);
                        }
                    }
                    break;
                }
                default:
                    throw Invalid($"{path}.type is invalid.");
            }
        }

        foreach (var (nodeId, inputs) in inputOrders)
        {
            if (inputs.Any(input => !nodeTypes.ContainsKey(input) || input == nodeId))
            {
                throw Invalid($"inputOrder of node {nodeId} references an unknown node.");
            }
        }

        ValidateConnections(connections, nodeTypes);

        return new AiCanvasDocumentSummary(
            json,
            documentBytes,
            nodes.Count,
            connections.Count,
            assetIds.Distinct(StringComparer.Ordinal).ToArray(),
            sourceTaskIds.Distinct().ToArray(),
            latestGenerated?.AssetId ?? firstImageAssetId);
    }

    public static bool IsAssetId(string? value) =>
        value is { Length: >= 12 and <= 40 }
        && value.StartsWith("AST", StringComparison.Ordinal)
        && value.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static void ValidateConnections(IReadOnlyList<JsonElement> connections, IReadOnlyDictionary<string, string> nodeTypes)
    {
        var connectionIds = new HashSet<string>(StringComparer.Ordinal);
        var edges = new HashSet<(string From, string To)>();
        var incoming = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < connections.Count; index++)
        {
            var path = $"connections[{index}]";
            var connection = ReadObject(connections[index], path, ConnectionKeys);
            if (!connectionIds.Add(RequireIdentifier(connection, "id", path)))
            {
                throw Invalid($"{path}.id is duplicated.");
            }
            var from = RequireIdentifier(connection, "from", path);
            var to = RequireIdentifier(connection, "to", path);
            if (!nodeTypes.TryGetValue(from, out var fromType) || !nodeTypes.TryGetValue(to, out var toType))
            {
                throw Invalid($"{path} references an unknown node.");
            }
            // 图片/生图节点可连到生图节点作为参考图，也可连到图片节点表示派生来源（切图、裁剪等）。
            if (from == to || toType is not ("generate" or "image") || fromType is not ("image" or "generate"))
            {
                throw Invalid($"{path} connects unsupported node types.");
            }
            if (!edges.Add((from, to)))
            {
                throw Invalid($"{path} is duplicated.");
            }
            incoming[to] = incoming.GetValueOrDefault(to) + 1;
            if (toType == "image" && incoming[to] > 1)
            {
                throw Invalid($"{path} gives an image node more than one source.");
            }
            if (toType == "generate" && incoming[to] > MaxGenerateInputs)
            {
                throw LimitExceeded("maxGenerateInputs", MaxGenerateInputs);
            }
        }

        // Kahn 拓扑排序：剩余未出队的节点说明连线成环。
        var pending = nodeTypes.Keys.ToDictionary(id => id, id => incoming.GetValueOrDefault(id), StringComparer.Ordinal);
        var outgoing = edges.ToLookup(edge => edge.From, edge => edge.To, StringComparer.Ordinal);
        var queue = new Queue<string>(pending.Where(x => x.Value == 0).Select(x => x.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var next in outgoing[current])
            {
                if (--pending[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }
        if (visited != nodeTypes.Count)
        {
            throw Invalid("connections must not form a cycle.");
        }
    }

    private static IReadOnlyList<string> ReadInputOrder(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        if (!data.TryGetValue("inputOrder", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxGenerateInputs)
        {
            throw Invalid($"{path}.inputOrder must be an array of at most {MaxGenerateInputs} node ids.");
        }
        var inputs = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (!IsIdentifier(value) || inputs.Contains(value!))
            {
                throw Invalid($"{path}.inputOrder contains an invalid or duplicated node id.");
            }
            inputs.Add(value!);
        }
        return inputs;
    }

    private static void ReadRuns(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        if (!data.TryGetValue("runs", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return;
        }
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxRunsPerNode)
        {
            throw Invalid($"{path}.runs must be an array of at most {MaxRunsPerNode} items.");
        }
        var index = 0;
        foreach (var run in element.EnumerateArray())
        {
            var runPath = $"{path}.runs[{index++}]";
            var fields = ReadObject(run, runPath, RunKeys);
            if (!fields.ContainsKey("idempotencyKey"))
            {
                throw Invalid($"{runPath}.idempotencyKey is required.");
            }
            OptionalIdempotencyKey(fields, "idempotencyKey", runPath);
            OptionalString(fields, "createdAt", runPath, 40);
            if (!fields.TryGetValue("taskIds", out var taskIds) || taskIds.ValueKind == JsonValueKind.Null)
            {
                continue;
            }
            if (taskIds.ValueKind != JsonValueKind.Array || taskIds.GetArrayLength() > MaxTaskIdsPerRun)
            {
                throw Invalid($"{runPath}.taskIds must be an array of at most {MaxTaskIdsPerRun} ids.");
            }
            foreach (var taskId in taskIds.EnumerateArray())
            {
                if (taskId.ValueKind != JsonValueKind.Number || !taskId.TryGetInt64(out var value) || value <= 0)
                {
                    throw Invalid($"{runPath}.taskIds contains an invalid id.");
                }
            }
        }
    }

    private static (long TaskId, string AssetId) PickLatest((long TaskId, string AssetId)? current, long taskId, string assetId) =>
        current is null || taskId > current.Value.TaskId ? (taskId, assetId) : current.Value;

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, string path, IReadOnlySet<string> allowedKeys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path} must be an object.");
        }
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedKeys.Contains(property.Name))
            {
                throw Invalid($"{path}.{property.Name} is not allowed.");
            }
            if (!fields.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{path}.{property.Name} is duplicated.");
            }
        }
        return fields;
    }

    private static IReadOnlyList<JsonElement> RequireArray(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        if (!fields.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path}.{name} must be an array.");
        }
        return element.EnumerateArray().ToArray();
    }

    private static string RequireString(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, int maxLength) =>
        OptionalString(fields, name, path, maxLength) ?? throw Invalid($"{path}.{name} is required.");

    private static string? OptionalString(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, int maxLength)
    {
        if (!fields.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path}.{name} must be a string.");
        }
        var value = element.GetString()!;
        if (value.Length > maxLength)
        {
            throw Invalid($"{path}.{name} must not exceed {maxLength} characters.");
        }
        return value;
    }

    private static void OptionalCode(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, int maxLength)
    {
        var value = OptionalString(fields, name, path, maxLength);
        if (value is not null && (value.Length == 0
            || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or ':' or '@' or '/' or '+'))))
        {
            throw Invalid($"{path}.{name} is invalid.");
        }
    }

    private static void OptionalIdempotencyKey(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        var value = OptionalString(fields, name, path, 128);
        if (value is not null && (value.Trim().Length < 8 || value.Any(char.IsControl)))
        {
            throw Invalid($"{path}.{name} must contain 8-128 non-control characters.");
        }
    }

    private static string RequireIdentifier(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        var value = RequireString(fields, name, path, 40);
        return IsIdentifier(value) ? value : throw Invalid($"{path}.{name} is invalid.");
    }

    private static bool IsIdentifier(string? value) =>
        value is { Length: >= 1 and <= 40 } && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static string RequireAssetId(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        var value = RequireString(fields, name, path, 40);
        return IsAssetId(value) ? value : throw Invalid($"{path}.{name} is invalid.");
    }

    private static double RequireNumber(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, double min, double max)
    {
        if (!fields.TryGetValue(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value)
            || !double.IsFinite(value)
            || value < min
            || value > max)
        {
            throw Invalid($"{path}.{name} must be a number between {min} and {max}.");
        }
        return value;
    }

    private static int RequireInt(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, int min, int max) =>
        (int)RequireLong(fields, name, path, min, max);

    private static long RequireLong(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, long min, long max) =>
        OptionalLong(fields, name, path, min, max) ?? throw Invalid($"{path}.{name} is required.");

    private static long? OptionalLong(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, long min, long max)
    {
        if (!fields.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value) || value < min || value > max)
        {
            throw Invalid($"{path}.{name} must be an integer between {min} and {max}.");
        }
        return value;
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, message);

    private static AppException LimitExceeded(string limit, int value) =>
        new(
            ErrorCodes.UnprocessableEntity,
            MachineErrorCodes.CanvasLimitExceeded,
            $"Canvas {limit} is {value}.",
            new { limit, value });
}
