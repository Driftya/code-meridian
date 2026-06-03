namespace CodeMeridian.Core.Agents;

/// <summary>
/// Carries per-request runtime state across the agent pipeline.
/// Create one per incoming request; do not share across requests.
/// </summary>
public sealed class AgentContext
{
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
    public string? ProjectContext { get; init; }
    public IReadOnlyList<AgentResponse> ConversationHistory { get; init; } = [];
    public Dictionary<string, object?> Properties { get; } = [];
}
