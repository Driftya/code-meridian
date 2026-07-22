using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindLargeNodesTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindLargeNodesAsync_WhenNoResults_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindLargeNodesAsync("Proj", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindLargeNodesAsync("Proj");

        result.Should().Contain("No large classes");
        result.Should().Contain("Re-index");
    }

    [Fact]
    public async Task FindLargeNodesAsync_WithLargeClass_ReturnsTableWithLineCount()
    {
        var (sut, graph) = Build();
        var bigClass = NodeWithLineCount("c1", "BigService", CodeNodeType.Class, "src/Big.cs", 10, 520);
        graph.FindLargeNodesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([bigClass]);

        var result = await sut.FindLargeNodesAsync();

        result.Should().Contain("## Large Node Analysis");
        result.Should().Contain("**1** oversized");
        result.Should().Contain("520");
        result.Should().Contain("`BigService`");
        result.Should().Contain("src/Big.cs");
        result.Should().Contain(":10");
        result.Should().Contain("SRP");
    }

    [Fact]
    public async Task FindLargeNodesAsync_WithMixedTypes_GroupsClassesAndMethodsSeparately()
    {
        var (sut, graph) = Build();
        graph.FindLargeNodesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 NodeWithLineCount("c1", "HugeClass", CodeNodeType.Class, lineCount: 600),
                 NodeWithLineCount("m1", "LongMethod", CodeNodeType.Method, lineCount: 80),
             ]);

        var result = await sut.FindLargeNodesAsync();

        result.Should().Contain("### Classes");
        result.Should().Contain("### Methods");
        result.Should().Contain("HugeClass");
        result.Should().Contain("LongMethod");
    }

    // ── GetContextForEditingAsync ─────────────────────────────────────────────


}

