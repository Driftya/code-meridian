using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindConnectionResultTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindConnectionResultAsync_ReturnsBoundedVersionedPathFacts()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("source", "target", Arg.Any<CancellationToken>())
            .Returns([
                (new CodeNode
                {
                    Id = "source",
                    Name = "Source",
                    Type = CodeNodeType.Class,
                    Namespace = "Example",
                    FilePath = "src/Source.cs",
                    LineNumber = 12,
                    ProjectContext = "Example",
                    SourceSnippet = "secret source body",
                    Properties = new Dictionary<string, string> { ["unbounded"] = "value" }
                }, "Calls"),
                (new CodeNode
                {
                    Id = "target",
                    Name = "Target",
                    Type = CodeNodeType.Method,
                    FilePath = "src/Target.cs",
                    ProjectContext = "Example"
                }, (string?)null)
            ]);

        var result = await sut.FindConnectionResultAsync("source", "target");

        result.ContractVersion.Should().Be("1.0");
        result.PathFound.Should().BeTrue();
        result.MaxHops.Should().Be(10);
        result.HopCount.Should().Be(1);
        result.Truncated.Should().BeFalse();
        result.Nodes.Should().HaveCount(2);
        result.Nodes[0].Should().Be(new ConnectionNodeResult(
            0, "source", "Source", "Class", "Example", "src/Source.cs", 12, "Example"));
        result.Edges.Should().ContainSingle().Which.Should().Be(new ConnectionEdgeResult(
            0, "source", "target", "Calls"));
        result.ToMarkdown(ContextDetailLevel.Compact).Should().Contain("—[Calls]→");
    }

    [Fact]
    public async Task FindConnectionResultAsync_WhenNoPath_ReturnsSchemaStableEmptyCollections()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("missing-a", "missing-b", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindConnectionResultAsync("missing-a", "missing-b");

        result.PathFound.Should().BeFalse();
        result.HopCount.Should().Be(0);
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.FrontendSignals.Should().BeEmpty();
        result.ToMarkdown(ContextDetailLevel.Full).Should().Contain("within 10 hops");
    }

    [Fact]
    public async Task FindConnectionResultAsync_AcceptsLegacyIncomingRelationshipAlignment()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("a", "c", Arg.Any<CancellationToken>())
            .Returns([
                (Node("a", "A", CodeNodeType.Class), (string?)null),
                (Node("b", "B", CodeNodeType.Method), "Calls"),
                (Node("c", "C", CodeNodeType.Class), "Uses")
            ]);

        var result = await sut.FindConnectionResultAsync("a", "c");

        result.Edges.Select(edge => edge.Relationship).Should().Equal("Calls", "Uses");
        result.ToMarkdown(ContextDetailLevel.Compact).Should().Contain("—[Uses]→");
    }
}
