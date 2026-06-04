namespace CodeMeridian.Core.Knowledge;

public sealed record KnowledgeDocument
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public string? Source { get; init; }
    public string? ProjectContext { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public float[]? Embedding { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}
