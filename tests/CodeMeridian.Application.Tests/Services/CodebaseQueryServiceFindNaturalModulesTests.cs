using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindNaturalModulesTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindNaturalModulesAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.louvain.stream"));

        var result = await sut.FindNaturalModulesAsync();

        result.Should().Contain("Community detection failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task FindNaturalModulesAsync_WithResults_GroupsByCommunity()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("a1", "ServiceA", CodeNodeType.Class, "src/A.cs"), 1L),
                 (Node("a2", "ServiceB", CodeNodeType.Class, "src/B.cs"), 1L),
                 (Node("b1", "HelperX",  CodeNodeType.Class, "src/X.cs"), 2L),
             ]);

        var result = await sut.FindNaturalModulesAsync();

        result.Should().Contain("## Natural Modules (Louvain)");
        result.Should().Contain("**2** organic communities");
        result.Should().Contain("### Production candidates (0)");
        result.Should().Contain("Hidden by default: 2 broader heuristic communities");
        result.Should().NotContain("Community 1");
        result.Should().NotContain("HelperX");
        result.Should().Contain("organic module boundaries");
    }

    [Fact]
    public async Task FindNaturalModulesAsync_WhenBroaderOutputEnabled_ShowsHeuristicCommunities()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false
            }
        });
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("a1", "ServiceA", CodeNodeType.Class, "src/A.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 1L),
                 (Node("a2", "ServiceB", CodeNodeType.Class, "src/B.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 1L),
                 (Node("b1", "HelperX",  CodeNodeType.Class, "src/X.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications"), 2L)
             ]);

        var result = await sut.FindNaturalModulesAsync();

        result.Should().Contain("### Broader heuristic matches (2)");
        result.Should().Contain("Community 1 (2 nodes)");
        result.Should().Contain("ServiceA");
        result.Should().Contain("ServiceB");
        result.Should().Contain("Community 2");
        result.Should().Contain("HelperX");
    }

    [Fact]
    public async Task FindNaturalModulesAsync_ProjectScopedAnalysisOptions_DoNotLeakAcrossCalls()
    {
        var defaultOptions = new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false
            }
        };
        var resolver = Substitute.For<IProjectAnalysisOptionsResolver>();
        resolver.ResolveAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ResolvedProjectAnalysisOptions>(new ResolvedProjectAnalysisOptions(
                new CodebaseAnalysisOptions
                {
                    Ranking = new RankingOptions
                    {
                        ProductionOnlyByDefault = true
                    }
                },
                new AnalysisOptionsResolutionMetadata(false, true, []))));
        resolver.ResolveAsync("Docs", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ResolvedProjectAnalysisOptions>(new ResolvedProjectAnalysisOptions(
                new CodebaseAnalysisOptions
                {
                    Ranking = new RankingOptions
                    {
                        ProductionOnlyByDefault = false
                    }
                },
                new AnalysisOptionsResolutionMetadata(false, true, []))));

        var (sut, graph, _) = Build(defaultOptions, resolver);
        var communityResults = new[]
        {
            (Node("a1", "ServiceA", CodeNodeType.Class, "src/A.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 1L),
            (Node("a2", "ServiceB", CodeNodeType.Class, "src/B.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 1L),
            (Node("b1", "HelperX", CodeNodeType.Class, "src/X.cs", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications"), 2L)
        };
        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>()).Returns(communityResults);
        graph.FindNaturalModulesAsync("Docs", Arg.Any<CancellationToken>()).Returns(communityResults);

        var shopResult = await sut.FindNaturalModulesAsync("Shop");
        var docsResult = await sut.FindNaturalModulesAsync("Docs");

        shopResult.Should().Contain("Hidden by default: 2 broader heuristic communities");
        shopResult.Should().NotContain("Community 1 (2 nodes)");
        docsResult.Should().Contain("### Broader heuristic matches (2)");
        docsResult.Should().Contain("Community 1 (2 nodes)");
        docsResult.Should().Contain("HelperX");
    }


}

