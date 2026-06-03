using CodeMeridian.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Application.Extensions;

/// <summary>
/// Thread-safe in-memory registry of external agent extensions.
/// In production, swap for a persistent implementation backed by Neo4j or Redis.
/// </summary>
public sealed class ExtensionRegistry : IExtensionRegistry
{
    private readonly Dictionary<string, AgentExtension> _extensions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public void Register(AgentExtension extension)
    {
        lock (_lock)
            _extensions[extension.Name] = extension;
    }

    public void Unregister(string name)
    {
        lock (_lock)
            _extensions.Remove(name);
    }

    public AgentExtension? Get(string name)
    {
        lock (_lock)
            return _extensions.GetValueOrDefault(name);
    }

    public IReadOnlyList<AgentExtension> GetAll()
    {
        lock (_lock)
            return [.. _extensions.Values];
    }

    public IReadOnlyList<AgentExtension> GetByCapability(string capability)
    {
        lock (_lock)
            return [.. _extensions.Values
                .Where(e => e.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))];
    }
}
