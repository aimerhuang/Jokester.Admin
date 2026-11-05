namespace jokester.admin.Infrastructure;

public sealed class AiCanvasOptions
{
    public const string SectionName = "AiCanvas";

    public int MaxProjectsPerUser { get; set; } = 50;

    public int MaxNodesPerProject { get; set; } = 300;

    public int MaxConnectionsPerProject { get; set; } = 600;

    public int MaxDocumentBytes { get; set; } = 1_048_576;
}
