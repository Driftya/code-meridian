namespace CodeMeridian.Sdk.Models;

/// <summary>
/// Payload for registering your project's agent as a CodeMeridian extension.
/// CodeMeridian will POST to <see cref="Endpoint"/> with an <see cref="AgentRequest"/>
/// and expect an <see cref="AgentResponse"/> back.
/// </summary>
public sealed record ExtensionRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Full URL of your agent's POST /ask endpoint.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Capabilities your agent handles (used for selective routing).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
