namespace CodeMeridian.Core.Agents;

public sealed record AgentRequest
{
    public required string Query { get; init; }

    /// <summary>Scopes the query to a specific project's knowledge graph.</summary>
    public string? ProjectContext { get; init; }

    /// <summary>Limits which sub-agents handle this request. Null means all available.</summary>
    public IReadOnlyList<string>? RequiredCapabilities { get; init; }

    public Dictionary<string, object?> Metadata { get; init; } = [];
}
