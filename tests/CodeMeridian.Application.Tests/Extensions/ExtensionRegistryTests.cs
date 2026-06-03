using CodeMeridian.Application.Extensions;
using CodeMeridian.Core.Extensions;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Extensions;

public sealed class ExtensionRegistryTests
{
    private readonly ExtensionRegistry _registry = new();

    private static AgentExtension MakeExtension(string name) => new()
    {
        Name = name,
        Description = $"{name} description",
        Endpoint = new Uri("http://example.com/agent"),
        Capabilities = ["domain-a", "domain-b"]
    };

    [Fact]
    public void Register_ThenGet_ReturnsExtension()
    {
        var ext = MakeExtension("ProjectAlpha");
        _registry.Register(ext);

        _registry.Get("ProjectAlpha").Should().NotBeNull();
        _registry.Get("projectalpha").Should().NotBeNull(); // case-insensitive
    }

    [Fact]
    public void Unregister_RemovesExtension()
    {
        _registry.Register(MakeExtension("ProjectAlpha"));
        _registry.Unregister("ProjectAlpha");

        _registry.Get("ProjectAlpha").Should().BeNull();
    }

    [Fact]
    public void Register_OverwritesExisting()
    {
        _registry.Register(MakeExtension("ProjectAlpha"));

        var updated = MakeExtension("ProjectAlpha") with
        {
            Description = "Updated description"
        };
        _registry.Register(updated);

        _registry.Get("ProjectAlpha")!.Description.Should().Be("Updated description");
    }

    [Fact]
    public void GetByCapability_ReturnsMatchingExtensions()
    {
        _registry.Register(MakeExtension("A"));
        _registry.Register(new AgentExtension
        {
            Name = "B",
            Description = "B agent",
            Endpoint = new Uri("http://b.example.com"),
            Capabilities = ["unrelated"]
        });

        var results = _registry.GetByCapability("domain-a");

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("A");
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        _registry.Register(MakeExtension("A"));
        _registry.Register(MakeExtension("B"));

        _registry.GetAll().Should().HaveCount(2);
    }
}
