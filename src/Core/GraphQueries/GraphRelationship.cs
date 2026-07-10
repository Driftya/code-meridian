namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphRelationship
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public IReadOnlyList<GraphProperty> Properties { get; init; } = [];
}
