using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceGetBetweennessTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task GetBetweennessAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.betweenness.stream"));

        var result = await sut.GetBetweennessAsync();

        result.Should().Contain("Betweenness Centrality failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task GetBetweennessAsync_WithResults_ReturnsBridgeTable()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("b1", "EventBus", CodeNodeType.Class, "src/Bus.cs"), 1540.0),
                       (Node("b2", "Mediator", CodeNodeType.Class), 321.0)]);

        var result = await sut.GetBetweennessAsync();

        result.Should().Contain("## Betweenness Centrality");
        result.Should().Contain("Bridge Nodes");
        result.Should().Contain("EventBus");
        result.Should().Contain("1540");
        result.Should().Contain("connective tissue");
    }

    [Fact]
    public async Task GetBetweennessAsync_OrdersScoresDescendingWithinProductionSection()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("b1", "Lower", CodeNodeType.Class, "src/Lower.cs"), 20.0),
                (Node("b2", "Highest", CodeNodeType.Class, "src/Highest.cs"), 90.0),
                (Node("b3", "Middle", CodeNodeType.Class, "src/Middle.cs"), 50.0)
            ]);

        var result = await sut.GetBetweennessAsync();

        result.IndexOf("`Highest`", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("`Middle`", StringComparison.Ordinal));
        result.IndexOf("`Middle`", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("`Lower`", StringComparison.Ordinal));
    }

    // ── FindNaturalModulesAsync ───────────────────────────────────────────────


}
