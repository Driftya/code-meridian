using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindDownstreamTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindDownstreamAsync_OrdersProductionRowsByDistanceAscending()
    {
        var (sut, graph) = Build();
        var farther = Node("far", "FartherDependency", CodeNodeType.Method, "src/Far.cs", lineCount: 200, fileRole: IndexedFileRole.Source);
        var nearer = Node("near", "NearerDependency", CodeNodeType.Method, "src/Near.cs", lineCount: 10, fileRole: IndexedFileRole.Source);
        graph.FindDownstreamAsync("target", 5, Arg.Any<CancellationToken>()).Returns([(farther, 4), (nearer, 1)]);

        var result = await sut.FindDownstreamAsync("target");

        result.IndexOf("NearerDependency", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("FartherDependency", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindDownstreamAsync_WhenNoDownstream_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindDownstreamAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindDownstreamAsync("Method:Foo.Bar()");

        result.Should().Contain("No downstream dependencies found");
        result.Should().Contain("Method:Foo.Bar()");
    }

    [Fact]
    public async Task FindDownstreamAsync_DefaultNoiseReduction_HidesSuppressedTestDependencies()
    {
        var (sut, graph) = Build();
        graph.FindDownstreamAsync("Method:Foo.Bar()", 3, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("d1", "DepService", CodeNodeType.Class, "src/Dep.cs", fileRole: IndexedFileRole.Source), 1),
                 (Node("d2", "DepServiceTests", CodeNodeType.Class, "tests/DepServiceTests.cs", fileRole: IndexedFileRole.Test), 2)
             ]);

        var result = await sut.FindDownstreamAsync("Method:Foo.Bar()", depth: 3);

        result.Should().Contain("## Downstream Blast Radius");
        result.Should().Contain("**2** elements");
        result.Should().Contain("### Production dependencies (1)");
        result.Should().Contain("DepService");
        result.Should().Contain("src/Dep.cs");
        result.Should().NotContain("### Suppressed test/config noise");
        result.Should().NotContain("DepServiceTests");
        result.Should().Contain("Hidden by default: 0 broader heuristic matches, 1 suppressed noise node");
        result.Should().Contain("find_impact");
    }

    [Fact]
    public async Task FindDownstreamAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedDependencies()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                IncludeBroaderHeuristicMatches = true,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindDownstreamAsync("Method:Foo.Bar()", 3, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("d1", "DepService", CodeNodeType.Class, "src/Dep.cs", fileRole: IndexedFileRole.Source), 1),
                 (Node("d2", "POST /deps", CodeNodeType.ApiEndpoint, "src/DepsEndpoint.cs", fileRole: IndexedFileRole.Source), 2),
                 (Node("d3", "DepServiceTests", CodeNodeType.Class, "tests/DepServiceTests.cs", fileRole: IndexedFileRole.Test), 3)
             ]);

        var result = await sut.FindDownstreamAsync("Method:Foo.Bar()", depth: 3);

        result.Should().Contain("### Production dependencies (1)");
        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed test/config noise (1)");
        result.Should().Contain("POST /deps");
        result.Should().Contain("DepServiceTests");
    }
    // ── FindCyclesAsync ───────────────────────────────────────────────────────


}

