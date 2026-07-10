namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphNode
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Labels { get; init; }
    public required string PrimaryLabel { get; init; }
    public string? ProjectContext { get; init; }
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? FilePath { get; init; }
    public IReadOnlyList<GraphProperty> Properties { get; init; } = [];
}
