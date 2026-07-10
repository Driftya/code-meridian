namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphRelationshipFilter
{
    public IReadOnlyList<string> RelationshipTypes { get; init; } = [];
    public string? ProjectContext { get; init; }
    public IReadOnlyDictionary<string, string> PropertyEquals { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> PropertyContains { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> FromNodeIds { get; init; } = [];
    public IReadOnlyList<string> ToNodeIds { get; init; } = [];
}
