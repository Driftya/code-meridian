namespace CodeMeridian.Core.CodeGraph;

public sealed record SqlFileGraph
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public IReadOnlyList<CodeNode> Nodes { get; init; } = [];
    public IReadOnlyList<CodeEdge> Edges { get; init; } = [];
}
