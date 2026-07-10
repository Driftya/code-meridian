namespace CodeMeridian.McpServer.GraphQl;

public sealed record GraphRelationshipFilterInput
{
    public IReadOnlyList<string>? RelationshipTypes { get; init; }
    public string? ProjectContext { get; init; }
    public IReadOnlyList<GraphPropertyFilterInput>? PropertyEquals { get; init; }
    public IReadOnlyList<GraphPropertyFilterInput>? PropertyContains { get; init; }
    public IReadOnlyList<string>? FromNodeIds { get; init; }
    public IReadOnlyList<string>? ToNodeIds { get; init; }
}
