namespace CodeMeridian.Application.Services;

public sealed record ConnectionEdgeResult(
    int Order,
    string SourceId,
    string TargetId,
    string Relationship)
{
    public string EvidenceKind { get; init; } = "unknown";
    public string? EvidenceReason { get; init; }
    public string? Resolver { get; init; }
    public string? SourceFilePath { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public int? SourceEndLine { get; init; }
    public int? SourceEndColumn { get; init; }
}
