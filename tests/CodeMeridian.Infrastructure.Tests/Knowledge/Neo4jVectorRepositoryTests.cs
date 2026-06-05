using System.Reflection;
using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Tests.Knowledge;

public sealed class Neo4jVectorRepositoryTests
{
    [Fact]
    public void ExtractMentionIds_SplitsPrimitiveMetadataValues()
    {
        var result = InvokeExtractMentionIds(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["relatedNodeIds"] = "Method:Foo.Bar,Method:Baz.Qux"
        });

        result.Should().ContainInOrder("Method:Foo.Bar", "Method:Baz.Qux");
    }

    [Fact]
    public void ReadMetadata_RehydratesPrimitiveProperties()
    {
        var props = new Dictionary<string, object?>
        {
            ["relatedNodeIds"] = "Method:Foo.Bar,Method:Baz.Qux",
            ["metadataKind"] = "agent-note"
        };

        var result = InvokeReadMetadata(props);

        result.Should().ContainKey("relatedNodeIds");
        result["relatedNodeIds"].Should().Be("Method:Foo.Bar,Method:Baz.Qux");
        result.Should().ContainKey("kind");
        result["kind"].Should().Be("agent-note");
    }

    [Fact]
    public void ReadMetadata_WithNoMetadata_ReturnsEmptyDictionary()
    {
        var result = InvokeReadMetadata(new Dictionary<string, object?>());

        result.Should().BeEmpty();
    }

    private static List<string> InvokeExtractMentionIds(IReadOnlyDictionary<string, string> metadata)
    {
        var method = typeof(Neo4jVectorRepository).GetMethod(
            "ExtractMentionIds",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        return (List<string>)method!.Invoke(null, new object?[] { metadata })!;
    }

    private static Dictionary<string, string> InvokeReadMetadata(IReadOnlyDictionary<string, object?> props)
    {
        var method = typeof(Neo4jVectorRepository).GetMethod(
            "ReadMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        return (Dictionary<string, string>)method!.Invoke(null, new object?[] { props })!;
    }
}
