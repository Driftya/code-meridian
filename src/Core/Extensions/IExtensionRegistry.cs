namespace CodeMeridian.Core.Extensions;

public interface IExtensionRegistry
{
    void Register(AgentExtension extension);
    void Unregister(string name);
    AgentExtension? Get(string name);
    IReadOnlyList<AgentExtension> GetAll();
    IReadOnlyList<AgentExtension> GetByCapability(string capability);
}
