namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphNeighbor
{
    public required GraphNode Node { get; init; }
    public required GraphRelationship Relationship { get; init; }
    public required int Distance { get; init; }
}
