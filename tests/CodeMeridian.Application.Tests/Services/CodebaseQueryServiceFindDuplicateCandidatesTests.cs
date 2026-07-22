using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindDuplicateCandidatesTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindDuplicateCandidatesAsync_WhenNoCandidates_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph.FindDuplicateCandidatesAsync(null, null, null, 5, 0.88, true, 20, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindDuplicateCandidatesAsync();

        result.Should().Contain("No duplicate-code candidates found");
        result.Should().Contain("Embeddings must be stored");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_DefaultNoiseReduction_ShowsLowRiskExtractionCandidatesFirst()
    {
        var (sut, graph) = Build();
        var source = Node("m1", "CalculateTotal", CodeNodeType.Method, "src/Application/Orders.cs", line: 42, lineCount: 18, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Orders");
        var candidate = Node("m2", "ComputeTotal", CodeNodeType.Method, "src/Application/Billing.cs", line: 87, lineCount: 20, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Billing");
        var incidental = Node("m3", "ComputeLedgerTotal", CodeNodeType.Method, "src/Infrastructure/Ledger.cs", line: 12, lineCount: 21, fileRole: IndexedFileRole.Source, @namespace: "Shop.Infrastructure.Billing");

        graph.FindDuplicateCandidatesAsync("Shop", "Domain", CodeNodeType.Method, 10, 0.90, true, 20, Arg.Any<CancellationToken>())
             .Returns([
                 new DuplicateCandidate(source, candidate, 0.94, 1, 2, true, false),
                 new DuplicateCandidate(source, incidental, 0.90, 4, 3, false, false)
             ]);

        var result = await sut.FindDuplicateCandidatesAsync(
            projectContext: "Shop",
            namespaceFilter: "Domain",
            nodeType: "Method",
            minLineCount: 10,
            minSimilarity: 0.90);

        result.Should().Contain("## Duplicate-Code Candidates - Shop");
        result.Should().Contain("### Low-risk extraction candidates (1)");
        result.Should().Contain("94.0%");
        result.Should().Contain("CalculateTotal");
        result.Should().Contain("ComputeTotal");
        result.Should().Contain("18/20 lines");
        result.Should().Contain("Medium (3 callers)");
        result.Should().Contain("source only");
        result.Should().NotContain("### Broader incidental similarity");
        result.Should().NotContain("ComputeLedgerTotal");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 0 suppressed noise nodes");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WhenBroaderOutputEnabled_ShowsIncidentalAndSuppressedFamilies()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                IncludeBroaderHeuristicMatches = true,
                IncludeSuppressedNoise = true
            }
        });
        var source = Node("m1", "CalculateTotal", CodeNodeType.Method, "src/Application/Orders.cs", line: 42, lineCount: 18, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Orders");
        var primary = Node("m2", "ComputeTotal", CodeNodeType.Method, "src/Application/Billing.cs", line: 87, lineCount: 20, fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Billing");
        var incidental = Node("m3", "ComputeLedgerTotal", CodeNodeType.Method, "src/Infrastructure/Ledger.cs", line: 12, lineCount: 21, fileRole: IndexedFileRole.Source, @namespace: "Shop.Infrastructure.Billing");
        var testOnly = Node("m4", "CalculateTotalTests", CodeNodeType.Method, "tests/Application/CalculateTotalTests.cs", line: 15, lineCount: 20, fileRole: IndexedFileRole.Test, @namespace: "Shop.Tests.Orders");

        graph.FindDuplicateCandidatesAsync("Shop", "Domain", CodeNodeType.Method, 10, 0.90, false, 20, Arg.Any<CancellationToken>())
             .Returns([
                 new DuplicateCandidate(source, primary, 0.94, 1, 2, true, false),
                 new DuplicateCandidate(source, incidental, 0.90, 4, 3, false, false),
                 new DuplicateCandidate(source, testOnly, 0.95, 0, 0, true, true)
             ]);

        var result = await sut.FindDuplicateCandidatesAsync(
            projectContext: "Shop",
            namespaceFilter: "Domain",
            nodeType: "Method",
            minLineCount: 10,
            minSimilarity: 0.90,
            excludeTests: false);

        result.Should().Contain("### Low-risk extraction candidates (1)");
        result.Should().Contain("### Broader incidental similarity (1)");
        result.Should().Contain("### Suppressed test/config similarity (0)");
        result.Should().Contain("ComputeLedgerTotal");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_ExtremeSizeMismatch_IsNotLowRisk()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions { IncludeBroaderHeuristicMatches = true },
            DuplicateNoise = new DuplicateNoiseOptions { MinimumPrimarySizeRatio = 0.5 }
        });
        var large = Node("large", "ProcessBatch", CodeNodeType.Method, "src/Application/Batch.cs", lineCount: 223, fileRole: IndexedFileRole.Source, @namespace: "Sample.Application");
        var small = Node("small", "ProcessItem", CodeNodeType.Method, "src/Application/Item.cs", lineCount: 16, fileRole: IndexedFileRole.Source, @namespace: "Sample.Application");
        graph.FindDuplicateCandidatesAsync(null, null, CodeNodeType.Method, 5, 0.88, true, 20, Arg.Any<CancellationToken>())
            .Returns([new DuplicateCandidate(large, small, 0.98, 0, 0, true, true)]);

        var result = await sut.FindDuplicateCandidatesAsync(nodeType: "Method");

        result.Should().Contain("### Low-risk extraction candidates (0)");
        result.Should().Contain("### Broader incidental similarity (1)");
        result.Should().Contain("line-count ratio 0.07 is below 0.50");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WithInvalidNodeType_ReturnsGuidance()
    {
        var (sut, _) = Build();

        var result = await sut.FindDuplicateCandidatesAsync(nodeType: "File");

        result.Should().Contain("Unknown duplicate candidate node type");
        result.Should().Contain("Method");
        result.Should().Contain("Class");
        result.Should().Contain("ExternalConcept");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WithFrontendStyleDeclarations_ReturnsNearDuplicateClusters()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([
                CreateFrontendStyleDeclaration("n1", ".card", "src/web/Card.scss", 10, "padding", "1rem"),
                CreateFrontendStyleDeclaration("n2", ".card--wide", "src/web/Card.scss", 16, "padding", "16px"),
                CreateFrontendStyleDeclaration("n3", ".card--hero", "src/web/HeroCard.scss", 4, "padding", "1.02rem"),
                CreateFrontendStyleDeclaration("n4", ".panel", "src/web/Panel.scss", 9, "color", "#ff0000"),
                CreateFrontendStyleDeclaration("n5", ".panel--muted", "src/web/Panel.scss", 14, "color", "rgb(250, 4, 4)")
            ]);

        var result = await sut.FindDuplicateCandidatesAsync(
            projectContext: "Shop.Web",
            nodeType: "ExternalConcept");

        result.Should().Contain("## Frontend Style Near-Duplicate Clusters - Shop.Web");
        result.Should().Contain("`padding`");
        result.Should().Contain("bounded numeric/unit drift");
        result.Should().Contain("16px");
        result.Should().Contain("1.02rem");
        result.Should().Contain("base-class variant review around `.card`");
        result.Should().Contain("colors within Euclidean RGBA distance");
        result.Should().Contain("`color`");
        result.Should().Contain(".panel--muted");
    }


}

