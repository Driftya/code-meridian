namespace CodeMeridian.Core.CodeGraph;

public sealed record CodeNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required CodeNodeType Type { get; init; }
    public string? Namespace { get; init; }
    public string? FilePath { get; init; }
    public int? LineNumber { get; init; }
    public int? LineCount { get; init; }
    public string? Summary { get; init; }
    public string? ProjectContext { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Dictionary<string, string> Properties { get; init; } = [];

    /// <summary>Number of times this node has been re-indexed. Used for churn analysis.</summary>
    public int? ChangeCount { get; init; }

    /// <summary>Optional vector embedding for native Neo4j semantic similarity search.</summary>
    public float[]? Embedding { get; init; }
}

public enum CodeNodeType
{
    Namespace,
    Class,
    Interface,
    Method,
    Property,
    Field,
    Enum,
    File,
    Module,
    ExternalConcept,
    DatabaseTable,
    ApiEndpoint,
    MessageTopic,
    ExternalService,
    Diagnostic
}
