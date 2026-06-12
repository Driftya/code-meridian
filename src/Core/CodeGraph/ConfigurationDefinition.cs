namespace CodeMeridian.Core.CodeGraph;

public sealed record ConfigurationDefinition
{
    public required CodeNode FileNode { get; init; }
    public required CodeNode EntryNode { get; init; }
    public required CodeNode KeyNode { get; init; }
    public required string RelationshipType { get; init; }
    public string? RawKey { get; init; }
    public string? SourceKind { get; init; }
    public string? ValuePreview { get; init; }
}
