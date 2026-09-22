namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphRelationship
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public CodeMeridian.Core.CodeGraph.EdgeEvidenceKind EvidenceKind { get; init; } = CodeMeridian.Core.CodeGraph.EdgeEvidenceKind.Unknown;
    public string? EvidenceReason { get; init; }
    public string? Resolver { get; init; }
    public string? SourceFilePath { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public int? SourceEndLine { get; init; }
    public int? SourceEndColumn { get; init; }
    public IReadOnlyDictionary<string, string>? EvidenceDetails { get; init; }
    public IReadOnlyList<GraphProperty> Properties { get; init; } = [];
}
