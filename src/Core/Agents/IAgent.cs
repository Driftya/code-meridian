namespace CodeMeridian.Core.Agents;

public interface IAgent
{
    string Name { get; }
    string Description { get; }

    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        AgentContext context,
        CancellationToken cancellationToken = default);
}
