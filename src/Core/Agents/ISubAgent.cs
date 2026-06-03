namespace CodeMeridian.Core.Agents;

/// <summary>
/// A focused sub-agent that handles a specific capability area.
/// Sub-agents are discovered by the orchestrator and invoked in parallel.
/// </summary>
public interface ISubAgent : IAgent
{
    IReadOnlyList<string> Capabilities { get; }
}
