namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphProperty
{
    public required string Key { get; init; }
    public string? Value { get; init; }
    public GraphPropertyValueKind ValueKind { get; init; } = GraphPropertyValueKind.String;
}
