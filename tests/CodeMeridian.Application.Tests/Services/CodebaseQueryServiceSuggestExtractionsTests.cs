using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceSuggestExtractionsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task SuggestExtractionsAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.louvain.stream"));

        var result = await sut.SuggestExtractionsAsync();

        result.Should().Contain("Extraction suggestion failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_WithCommunitySignals_ReturnsExtractionCandidates()
    {
        var (sut, graph) = Build();
        var anchor = Node(
            "a1",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 340,
            sourceHash: "abc123",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var helper = Node(
            "a2",
            "RetryPolicyBuilder",
            CodeNodeType.Class,
            "src/Application/Payments/RetryPolicyBuilder.cs",
            line: 8,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 90,
            sourceHash: "def456",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var serializer = Node(
            "a3",
            "PaymentMapper",
            CodeNodeType.Method,
            "src/Application/Payments/PaymentMapper.cs",
            line: 20,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 30,
            sourceHash: "ghi789",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var otherCommunity = Node(
            "b1",
            "EmailTemplateBuilder",
            CodeNodeType.Class,
            "src/Application/Notifications/EmailTemplateBuilder.cs",
            line: 6,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40,
            sourceHash: "jkl012",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Notifications");
        var test = Node("t1", "PaymentFacadeTests", CodeNodeType.Class, "tests/Payments/PaymentFacadeTests.cs", 5, "Shop", fileRole: IndexedFileRole.Test);

        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([
                (anchor, 1L),
                (helper, 1L),
                (serializer, 1L),
                (otherCommunity, 2L)
            ]);
        graph.FindHotspotsAsync("Shop", 50, Arg.Any<CancellationToken>())
            .Returns([(anchor, 6)]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>())
            .Returns([(anchor, 340, 6)]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([serializer]);
        graph.FindRelatedTestsAsync(anchor.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);
        graph.FindRelatedTestsAsync(helper.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(serializer.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.SuggestExtractionsAsync("Shop", limit: 5);

        result.Should().Contain("## Refactor Extraction Candidates - Shop");
        result.Should().Contain("### Primary candidates (1)");
        result.Should().Contain("`Shop.Application.Payments`");
        result.Should().Contain("PaymentFacade");
        result.Should().Contain("PaymentFacadeTests");
        result.Should().Contain("coverage gaps");
        result.Should().Contain("anchor fan-in 6");
        result.Should().Contain("large (340 lines)");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_DefaultNoiseReduction_HidesWeakCandidates()
    {
        var (sut, graph) = Build();
        var strongA = Node("a1", "PaymentFacade", CodeNodeType.Class, "src/Application/Payments/PaymentFacade.cs", project: "Shop", lineCount: 340, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongB = Node("a2", "RetryPolicyBuilder", CodeNodeType.Class, "src/Application/Payments/RetryPolicyBuilder.cs", project: "Shop", lineCount: 90, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongC = Node("a3", "PaymentMapper", CodeNodeType.Method, "src/Application/Payments/PaymentMapper.cs", project: "Shop", lineCount: 30, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var weakA = Node("b1", "EmailTemplateBuilder", CodeNodeType.Class, "src/Application/Notifications/EmailTemplateBuilder.cs", project: "Shop", lineCount: 40, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakB = Node("b2", "EmailFormatter", CodeNodeType.Method, "src/Application/Notifications/EmailFormatter.cs", project: "Shop", lineCount: 20, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakC = Node("b3", "EmailPreviewBuilder", CodeNodeType.Method, "src/Application/Notifications/EmailPreviewBuilder.cs", project: "Shop", lineCount: 18, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var strongTest = Node("t1", "PaymentFacadeTests", CodeNodeType.Class, "tests/Payments/PaymentFacadeTests.cs", project: "Shop", fileRole: IndexedFileRole.Test);

        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([
                (strongA, 1L),
                (strongB, 1L),
                (strongC, 1L),
                (weakA, 2L),
                (weakB, 2L),
                (weakC, 2L)
            ]);
        graph.FindHotspotsAsync("Shop", 50, Arg.Any<CancellationToken>()).Returns([(strongA, 6)]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>()).Returns([(strongA, 340, 6)]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>()).Returns([strongC]);
        graph.FindRelatedTestsAsync(strongA.Id, "Shop", Arg.Any<CancellationToken>()).Returns([(strongTest, "direct")]);
        graph.FindRelatedTestsAsync(strongB.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(strongC.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakA.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakB.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakC.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);

        var result = await sut.SuggestExtractionsAsync("Shop", limit: 5);

        result.Should().Contain("### Primary candidates (1)");
        result.Should().Contain("Hidden by default: 1 weaker heuristic candidate.");
        result.Should().NotContain("### Weaker heuristic candidates");
        result.Should().NotContain("EmailTemplateBuilder");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_WhenBroaderOutputEnabled_ShowsWeakCandidatesSection()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false
            }
        });
        var strongA = Node("a1", "PaymentFacade", CodeNodeType.Class, "src/Application/Payments/PaymentFacade.cs", project: "Shop", lineCount: 340, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongB = Node("a2", "RetryPolicyBuilder", CodeNodeType.Class, "src/Application/Payments/RetryPolicyBuilder.cs", project: "Shop", lineCount: 90, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongC = Node("a3", "PaymentMapper", CodeNodeType.Method, "src/Application/Payments/PaymentMapper.cs", project: "Shop", lineCount: 30, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var weakA = Node("b1", "EmailTemplateBuilder", CodeNodeType.Class, "src/Application/Notifications/EmailTemplateBuilder.cs", project: "Shop", lineCount: 40, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakB = Node("b2", "EmailFormatter", CodeNodeType.Method, "src/Application/Notifications/EmailFormatter.cs", project: "Shop", lineCount: 20, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakC = Node("b3", "EmailPreviewBuilder", CodeNodeType.Method, "src/Application/Notifications/EmailPreviewBuilder.cs", project: "Shop", lineCount: 18, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var strongTest = Node("t1", "PaymentFacadeTests", CodeNodeType.Class, "tests/Payments/PaymentFacadeTests.cs", project: "Shop", fileRole: IndexedFileRole.Test);

        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([
                (strongA, 1L),
                (strongB, 1L),
                (strongC, 1L),
                (weakA, 2L),
                (weakB, 2L),
                (weakC, 2L)
            ]);
        graph.FindHotspotsAsync("Shop", 50, Arg.Any<CancellationToken>()).Returns([(strongA, 6)]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>()).Returns([(strongA, 340, 6)]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>()).Returns([strongC]);
        graph.FindRelatedTestsAsync(strongA.Id, "Shop", Arg.Any<CancellationToken>()).Returns([(strongTest, "direct")]);
        graph.FindRelatedTestsAsync(strongB.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(strongC.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakA.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakB.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakC.Id, "Shop", Arg.Any<CancellationToken>()).Returns([]);

        var result = await sut.SuggestExtractionsAsync("Shop", limit: 5);

        result.Should().Contain("### Weaker heuristic candidates (1)");
        result.Should().Contain("EmailTemplateBuilder");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_ProjectScopedAnalysisOptions_DoNotLeakAcrossCalls()
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
        var strongA = Node("a1", "PaymentFacade", CodeNodeType.Class, "src/Application/Payments/PaymentFacade.cs", project: "Shop", lineCount: 340, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongB = Node("a2", "RetryPolicyBuilder", CodeNodeType.Class, "src/Application/Payments/RetryPolicyBuilder.cs", project: "Shop", lineCount: 90, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var strongC = Node("a3", "PaymentMapper", CodeNodeType.Method, "src/Application/Payments/PaymentMapper.cs", project: "Shop", lineCount: 30, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments");
        var weakA = Node("b1", "EmailTemplateBuilder", CodeNodeType.Class, "src/Application/Notifications/EmailTemplateBuilder.cs", project: "Shop", lineCount: 40, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakB = Node("b2", "EmailFormatter", CodeNodeType.Method, "src/Application/Notifications/EmailFormatter.cs", project: "Shop", lineCount: 20, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var weakC = Node("b3", "EmailPreviewBuilder", CodeNodeType.Method, "src/Application/Notifications/EmailPreviewBuilder.cs", project: "Shop", lineCount: 18, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Notifications");
        var strongTest = Node("t1", "PaymentFacadeTests", CodeNodeType.Class, "tests/Payments/PaymentFacadeTests.cs", project: "Shop", fileRole: IndexedFileRole.Test);

        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([
                (strongA, 1L),
                (strongB, 1L),
                (strongC, 1L),
                (weakA, 2L),
                (weakB, 2L),
                (weakC, 2L)
            ]);
        graph.FindNaturalModulesAsync("Docs", Arg.Any<CancellationToken>())
            .Returns([
                (strongA, 1L),
                (strongB, 1L),
                (strongC, 1L),
                (weakA, 2L),
                (weakB, 2L),
                (weakC, 2L)
            ]);
        graph.FindHotspotsAsync(Arg.Any<string?>(), 50, Arg.Any<CancellationToken>()).Returns([(strongA, 6)]);
        graph.FindGodClassesAsync(Arg.Any<string?>(), 300, 3, Arg.Any<CancellationToken>()).Returns([(strongA, 340, 6)]);
        graph.FindCoverageGapsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([strongC]);
        graph.FindRelatedTestsAsync(strongA.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([(strongTest, "direct")]);
        graph.FindRelatedTestsAsync(strongB.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(strongC.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakA.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakB.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(weakC.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);

        var shopResult = await sut.SuggestExtractionsAsync("Shop", limit: 5);
        var docsResult = await sut.SuggestExtractionsAsync("Docs", limit: 5);

        shopResult.Should().Contain("Hidden by default: 1 weaker heuristic candidate.");
        shopResult.Should().NotContain("### Weaker heuristic candidates");
        docsResult.Should().Contain("### Weaker heuristic candidates (1)");
        docsResult.Should().Contain("EmailTemplateBuilder");
    }

    // ── FindSimilarToNodeAsync ────────────────────────────────────────────────


}

