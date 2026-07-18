using CodeMeridian.Application.ClientExtensions;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.ClientExtensions;

public sealed class ClientExtensionServiceTests
{
    private readonly ClientExtensionService _sut = new();

    [Fact]
    public void GetContract_ReturnsLiveGraphQlMetadata()
    {
        var contract = _sut.GetContract();

        contract.Version.Should().Be("v1");
        contract.GraphQlEndpointPath.Should().Be("/graphql");
        contract.SupportedAuthHeaders.Should().Contain(["Authorization", "X-CodeMeridian-ApiKey"]);
        contract.MaxPageSize.Should().Be(100);
        contract.MaxTraversalDepth.Should().Be(3);
        contract.MaxAllowedFields.Should().Be(256);
        contract.MaxAllowedRecursionDepth.Should().Be(32);
        contract.ExecutionTimeoutSeconds.Should().Be(10);
        contract.SupportedNodeSortFields.Should().ContainInOrder("id", "name", "projectContext", "primaryLabel", "type", "filePath");
        contract.SupportedRelationshipSortFields.Should().ContainInOrder("id", "type", "fromNodeId", "toNodeId");
        contract.DocumentationPaths.Should().Contain("docs/graphql/README.md");
        contract.ExampleIds.Should().Contain("keyword-search");
    }

    [Fact]
    public void ListExamples_ReturnsDeterministicCheckedInExamples()
    {
        var examples = _sut.ListExamples();

        examples.Select(example => example.Id).Should().Equal(
            "schema-overview",
            "project-code-nodes",
            "keyword-search",
            "node-deep-dive",
            "neighborhood-walk",
            "relationships-by-type",
            "file-path-search",
            "keyword-category");

        examples.Single(example => example.Id == "keyword-search").VariablesTemplate.Should().Contain("\"text\": \"graphql\"");
    }

    [Fact]
    public void GetExample_UnknownId_ReturnsNull()
    {
        _sut.GetExample("missing-example").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetExample_BlankId_ReturnsNull(string? exampleId)
    {
        _sut.GetExample(exampleId!).Should().BeNull();
    }

    [Fact]
    public void GetExample_TrimsIdAndLoadsExpectedDocumentMetadata()
    {
        var example = _sut.GetExample("  KEYWORD-SEARCH  ");

        example.Should().NotBeNull();
        example!.Id.Should().Be("keyword-search");
        example.Name.Should().Be("Keyword search");
        example.GraphQlDocumentPath.Should().Be("docs/graphql/03-keyword-search.graphql");
        example.GraphQlDocument.Should().Contain("query KeywordSearch");
        example.VariablesTemplate.Should().NotBeNull();
        example.VariablesTemplate!.ReplaceLineEndings("\n").Should().ContainAll(
            "{",
            "\"text\": \"graphql\"",
            "\"limit\": 20",
            "}");
        example.ExpectedResultShape.Should().Contain("nodes[]");
        example.Notes.Should().ContainSingle();
    }

    [Fact]
    public void GetExample_WhenDocumentHasNoNitroVariables_TemplateIsNull()
    {
        var example = _sut.GetExample("schema-overview");

        example.Should().NotBeNull();
        example!.VariablesTemplate.Should().BeNull();
        example.GraphQlDocument.Should().Contain("query SchemaOverview");
    }
}
