using System.Text.Json;
using CodeMeridian.Application.ClientExtensions;
using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class StructuredToolWrappersTests
{
    [Fact]
    public async Task FindConnection_ReturnsFactsAndCompatibleMarkdown()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        var connection = new ConnectionAnalysisResult(
            "1.0",
            "source",
            "target",
            10,
            true,
            1,
            false,
            [new ConnectionNodeResult(0, "source", "Source", "Class", null, "src/Source.cs", 7, "Example"),
             new ConnectionNodeResult(1, "target", "Target", "Method", null, "src/Target.cs", 9, "Example")],
            [new ConnectionEdgeResult(0, "source", "target", "Calls")],
            []);
        queryService.FindConnectionResultAsync("source", "target", Arg.Any<CancellationToken>())
            .Returns(connection);

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindConnectionAsync("source", "target", ContextDetailLevel.Compact);

        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("—[Calls]→");
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.GetProperty("contractVersion").GetString().Should().Be("1.0");
        result.StructuredContent.Value.GetProperty("nodes").GetArrayLength().Should().Be(2);
        await queryService.Received(1).FindConnectionResultAsync(
            "source",
            "target",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClientExtensionContract_ReturnsEndpointAuthLimitsAndExamples()
    {
        var sut = new ClientExtensionTools(new ClientExtensionService());

        var result = sut.GetClientExtensionContract();
        var text = result.Content.Should().ContainSingle().Which
            .Should().BeOfType<TextContentBlock>().Which.Text;

        text.Should().Contain("# Client Extension Contract");
        text.Should().Contain("/graphql");
        text.Should().Contain("Authorization");
        text.Should().Contain("X-CodeMeridian-ApiKey");
        text.Should().Contain("Max page size: 100");
        text.Should().Contain("schema-overview");
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.GetProperty("graphQlEndpointPath").GetString().Should().Be("/graphql");
    }

    [Fact]
    public void ClientExtensionExamples_ReturnCheckedInQueriesAndStructuredFacts()
    {
        var sut = new ClientExtensionTools(new ClientExtensionService());

        var listed = sut.ListClientExtensionExamples();
        var listedText = listed.Content.Should().ContainSingle().Which
            .Should().BeOfType<TextContentBlock>().Which.Text;
        listedText.Should().Contain("keyword-search");
        listedText.Should().Contain("docs/graphql/03-keyword-search.graphql");
        listed.StructuredContent.Should().NotBeNull();
        listed.StructuredContent!.Value.ValueKind.Should().Be(JsonValueKind.Array);

        var example = sut.GetClientExtensionExample("keyword-search");
        var exampleText = example.Content.Should().ContainSingle().Which
            .Should().BeOfType<TextContentBlock>().Which.Text;
        exampleText.Should().Contain("# Client Extension Example: keyword-search");
        exampleText.Should().Contain("KeywordSearch");
        exampleText.Should().Contain("\"text\": \"graphql\"");
        exampleText.Should().Contain("Expected result shape");
        example.StructuredContent.Should().NotBeNull();
        example.StructuredContent!.Value.GetProperty("id").GetString().Should().Be("keyword-search");

        var missing = sut.GetClientExtensionExample("missing-example");
        missing.Content.Should().ContainSingle().Which
            .Should().BeOfType<TextContentBlock>().Which.Text
            .Should().Contain("Unknown client extension example");
        missing.StructuredContent.Should().BeNull();
    }
}
