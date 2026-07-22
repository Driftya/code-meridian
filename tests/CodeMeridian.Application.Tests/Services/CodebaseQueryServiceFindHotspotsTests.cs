using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindHotspotsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindHotspotsAsync_WhenEmpty_ReturnsNoHotspotsMessage()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync("Proj", Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindHotspotsAsync("Proj");

        result.Should().Contain("No hotspots found");
        result.Should().Contain("'Proj'");
    }

    [Fact]
    public async Task FindHotspotsAsync_WithResults_ContainsRankAndFanIn()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("h1", "CoreService", CodeNodeType.Class, "src/Core.cs"), 42),
                       (Node("h2", "UtilHelper", CodeNodeType.Class), 17)]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("## Coupling Hotspots");
        result.Should().Contain("### Production candidates (2)");
        result.Should().Contain("CoreService");
        result.Should().Contain("42");
        result.Should().Contain("UtilHelper");
        result.Should().Contain("17");
        result.Should().Contain("| 1 |"); // rank 1
        result.Should().Contain("| 2 |"); // rank 2
    }

    [Fact]
    public async Task FindHotspotsAsync_OrdersProductionRowsByFanInDescending()
    {
        var (sut, graph) = Build();
        var lower = Node("low", "LowerFanIn", CodeNodeType.Method, "src/Lower.cs", lineCount: 200, fileRole: IndexedFileRole.Source) with { ChangeCount = 99 };
        var higher = Node("high", "HigherFanIn", CodeNodeType.Method, "src/Higher.cs", lineCount: 10, fileRole: IndexedFileRole.Source) with { ChangeCount = 1 };
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([(lower, 4), (higher, 40)]);

        var result = await sut.FindHotspotsAsync();

        result.IndexOf("HigherFanIn", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("LowerFanIn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindHotspotsAsync_DefaultNoiseReduction_HidesBroaderAndSuppressedResults()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "BillingService", CodeNodeType.Class, "src/BillingService.cs", fileRole: IndexedFileRole.Source), 11),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 9),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 15)
            ]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("### Production candidates (1)");
        result.Should().Contain("BillingService");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 1 suppressed noise node.");
        result.Should().NotContain("### Broader heuristic matches");
        result.Should().NotContain("### Suppressed noise");
        result.Should().NotContain("Orders:Timeout");
    }

    [Fact]
    public async Task FindHotspotsAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "BillingService", CodeNodeType.Class, "src/BillingService.cs", fileRole: IndexedFileRole.Source), 11),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 9),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 15)
            ]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("Orders");
        result.Should().Contain("Orders:Timeout");
    }

    // ── FindConnectionAsync ───────────────────────────────────────────────────


}

