using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceGetPageRankTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task GetPageRankAsync_OrdersProductionRowsByScoreDescending()
    {
        var (sut, graph) = Build();
        var lower = Node("low", "LowerScore", CodeNodeType.Method, "src/Lower.cs", lineCount: 200, fileRole: IndexedFileRole.Source) with { ChangeCount = 99 };
        var higher = Node("high", "HigherScore", CodeNodeType.Method, "src/Higher.cs", lineCount: 10, fileRole: IndexedFileRole.Source) with { ChangeCount = 1 };
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([(lower, 0.1), (higher, 0.9)]);

        var result = await sut.GetPageRankAsync();

        result.IndexOf("HigherScore", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("LowerScore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPageRankAsync_WhenEmpty_ReturnsNoEdgesMessage()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("No results from PageRank");
        result.Should().Contain("Calls/Uses/DependsOn edges");
    }

    [Fact]
    public async Task GetPageRankAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.pageRank.stream"));

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("PageRank failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task GetPageRankAsync_WithResults_ReturnsRankedTable()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "CoreHub", CodeNodeType.Class, "src/Core.cs"), 0.87),
                       (Node("n2", "BaseRepo", CodeNodeType.Class), 0.43)]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("## PageRank");
        result.Should().Contain("### Production candidates (2)");
        result.Should().Contain("| 1 |");
        result.Should().Contain("CoreHub");
        result.Should().Contain("0.8700");
        result.Should().Contain("BaseRepo");
        result.Should().Contain("transitive call-graph influence");
    }

    [Fact]
    public async Task GetPageRankAsync_DefaultNoiseReduction_HidesBroaderAndSuppressedResults()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "CoreHub", CodeNodeType.Class, "src/CoreHub.cs", fileRole: IndexedFileRole.Source), 0.87),
                (Node("heur-1", "POST /orders", CodeNodeType.ApiEndpoint, "src/OrdersEndpoints.cs", fileRole: IndexedFileRole.Source), 0.91),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 0.99)
            ]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("### Production candidates (1)");
        result.Should().Contain("CoreHub");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 1 suppressed noise node.");
        result.Should().NotContain("### Broader heuristic matches");
        result.Should().NotContain("### Suppressed noise");
        result.Should().NotContain("Orders:Timeout");
    }

    [Fact]
    public async Task GetPageRankAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "CoreHub", CodeNodeType.Class, "src/CoreHub.cs", fileRole: IndexedFileRole.Source), 0.87),
                (Node("heur-1", "POST /orders", CodeNodeType.ApiEndpoint, "src/OrdersEndpoints.cs", fileRole: IndexedFileRole.Source), 0.91),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 0.99)
            ]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("POST /orders");
        result.Should().Contain("Orders:Timeout");
    }

    // ── GetBetweennessAsync ───────────────────────────────────────────────────


}

