using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Knowledge;

public sealed class Neo4jVectorRepositoryHelpersTests
{
    [Theory]
    [InlineData("  CODEMERIDIAN  ", "codemeridian")]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    public void Normalize_HandlesWhitespaceAndCase(string? value, string? expected)
    {
        Neo4jVectorRepositoryHelpers.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void ExtractMentionIds_UsesAlternateMetadataKeys_AndDeduplicates()
    {
        var result = Neo4jVectorRepositoryHelpers.ExtractMentionIds(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mentions"] = "Method:Foo.Bar;Method:Foo.Bar|Method:Baz.Qux"
        });

        result.Should().ContainInOrder("Method:Foo.Bar", "Method:Baz.Qux");
    }

    [Fact]
    public void ExtractRelatedDocumentIds_UsesReferenceAliases()
    {
        var result = Neo4jVectorRepositoryHelpers.ExtractRelatedDocumentIds(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["references"] = "docs/features/01.md\ndocs/features/02.md"
        });

        result.Should().ContainInOrder("docs/features/01.md", "docs/features/02.md");
    }

    [Fact]
    public void ReadMetadata_RehydratesPrimitiveProperties()
    {
        var props = new Dictionary<string, object?>
        {
            ["relatedNodeIds"] = "Method:Foo.Bar,Method:Baz.Qux",
            ["relatedDocumentIds"] = "docs/features/01.md,docs/features/02.md",
            ["metadataKind"] = "agent-note"
        };

        var result = Neo4jVectorRepositoryHelpers.ReadMetadata(props);

        result.Should().ContainKey("relatedNodeIds");
        result["relatedNodeIds"].Should().Be("Method:Foo.Bar,Method:Baz.Qux");
        result.Should().ContainKey("relatedDocumentIds");
        result["relatedDocumentIds"].Should().Be("docs/features/01.md,docs/features/02.md");
        result.Should().ContainKey("kind");
        result["kind"].Should().Be("agent-note");
    }

    [Fact]
    public void ReadMetadata_WithNoMetadata_ReturnsEmptyDictionary()
    {
        var result = Neo4jVectorRepositoryHelpers.ReadMetadata(new Dictionary<string, object?>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void MapToDocument_RehydratesDocumentMetadataAndTimestamps()
    {
        var node = Substitute.For<INode>();
        node.Properties.Returns(new Dictionary<string, object?>
        {
            ["id"] = "doc-1",
            ["content"] = "hello",
            ["source"] = "docs/feature.md",
            ["projectContext"] = "CodeMeridian",
            ["createdAt"] = 1_000L,
            ["updatedAt"] = 2_000L,
            ["relatedNodeIds"] = "Method:Foo.Bar",
            ["relatedDocumentIds"] = "docs/other.md",
            ["metadataKind"] = "note"
        });

        var result = Neo4jVectorRepositoryHelpers.MapToDocument(node);

        result.Id.Should().Be("doc-1");
        result.Content.Should().Be("hello");
        result.Source.Should().Be("docs/feature.md");
        result.ProjectContext.Should().Be("CodeMeridian");
        result.CreatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_000));
        result.UpdatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(2_000));
        result.Metadata.Should().ContainKey("relatedNodeIds");
        result.Metadata.Should().ContainKey("relatedDocumentIds");
        result.Metadata.Should().ContainKey("kind");
    }
}
