using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindHighChurnTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindHighChurnAsync_OrdersProductionRowsByChangeCountDescending()
    {
        var (sut, graph) = Build();
        var lower = Node("low", "LowerChurn", CodeNodeType.Method, "src/Lower.cs", lineCount: 200, fileRole: IndexedFileRole.Source);
        var higher = Node("high", "HigherChurn", CodeNodeType.Method, "src/Higher.cs", lineCount: 10, fileRole: IndexedFileRole.Source);
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>()).Returns([(lower, 4), (higher, 40)]);

        var result = await sut.FindHighChurnAsync();

        result.IndexOf("HigherChurn", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("LowerChurn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindHighChurnAsync_WhenNoHighChurn_ReturnsStableMessage()
    {
        var (sut, graph) = Build();
        graph.FindHighChurnAsync("Proj", Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindHighChurnAsync("Proj");

        result.Should().Contain("No high-churn nodes found");
        result.Should().Contain("'Proj'");
    }

    [Fact]
    public async Task FindHighChurnAsync_WithResults_ReturnsTable()
    {
        var (sut, graph) = Build();
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "HotFile", CodeNodeType.Class, "src/Hot.cs"), 12),
                       (Node("n2", "BusyHelper", CodeNodeType.Method), 5)]);

        var result = await sut.FindHighChurnAsync();

        result.Should().Contain("## High-Churn Nodes");
        result.Should().Contain("**2** nodes");
        result.Should().Contain("Production candidates are prioritized by default");
        result.Should().Contain("### Production candidates (2)");
        result.Should().Contain("HotFile");
        result.Should().Contain("12x");
        result.Should().Contain("5x");
        result.Should().Contain("find_hotspots");
    }

    [Fact]
    public async Task FindHighChurnAsync_WithRankingOptions_RanksProductionBeforeTests()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                PreferProductionOverTests = true,
                TestPathContains = ["tests/"]
            }
        });
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
            .Returns([
                (Node("t1", "Build", CodeNodeType.Method, "tests/ServiceTests.cs"), 20),
                (Node("p1", "RealService", CodeNodeType.Class, "src/RealService.cs"), 3)
            ]);

        var result = await sut.FindHighChurnAsync();

        result.Should().Contain("RealService");
        result.Should().NotContain("Build");
    }

    [Fact]
    public async Task FindHighChurnAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "RealService", CodeNodeType.Class, "src/RealService.cs", fileRole: IndexedFileRole.Source), 6),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 7),
                (Node("noise-1", "Build", CodeNodeType.Method, "tests/ServiceTests.cs", fileRole: IndexedFileRole.Test), 20)
            ]);

        var result = await sut.FindHighChurnAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("Orders");
        result.Should().Contain("Build");
    }

    // ── GetPageRankAsync ──────────────────────────────────────────────────────


}

