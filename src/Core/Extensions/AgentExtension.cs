namespace CodeMeridian.Core.Extensions;

/// <summary>
/// Represents an external agent registered as a CodeMeridian extension.
/// Any project can register its agent here; the orchestrator will call it
/// as a sub-agent when processing requests.
/// </summary>
public sealed record AgentExtension
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Full URL to the extension's POST /ask endpoint.</summary>
    public required Uri Endpoint { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsHealthy { get; set; } = true;
}
