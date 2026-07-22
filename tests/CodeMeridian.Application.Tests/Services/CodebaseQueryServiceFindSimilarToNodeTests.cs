using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindSimilarToNodeTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindSimilarToNodeAsync_WhenNoEmbeddings_ReturnsEmbeddingGuidance()
    {
        var (sut, graph) = Build();
        graph.FindSimilarToNodeAsync("Method:Foo.Bar", null, 10, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindSimilarToNodeAsync("Method:Foo.Bar");

        result.Should().Contain("No similar nodes found");
        result.Should().Contain("embeddingCsv");
        result.Should().Contain("ingest_code_node");
    }

    [Fact]
    public async Task FindSimilarToNodeAsync_DefaultNoiseReduction_PrefersSameFamilyProductionMatches()
    {
        var (sut, graph) = Build();
        graph.FindSimilarToNodeAsync("Method:Shop.Application.Foo.Bar", null, 10, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("s1", "SimilarMethod", CodeNodeType.Method, "src/Application/Similar.cs", project: "Shop", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 0.96),
                 (Node("s2", "RelatedService", CodeNodeType.Class, "src/Application/RelatedService.cs", project: "Shop", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 0.93),
                 (Node("s3", "PaymentFlowTests", CodeNodeType.Method, "tests/Application/PaymentFlowTests.cs", project: "Shop", fileRole: IndexedFileRole.Test, @namespace: "Shop.Tests.Payments"), 0.97)
             ]);

        var result = await sut.FindSimilarToNodeAsync("Method:Shop.Application.Foo.Bar");

        result.Should().Contain("## Semantically Similar Nodes");
        result.Should().Contain("**3** nodes");
        result.Should().Contain("### Same-family production matches (1)");
        result.Should().Contain("SimilarMethod");
        result.Should().Contain("96.0%");
        result.Should().NotContain("### Broader semantic matches");
        result.Should().NotContain("### Suppressed test/config matches");
        result.Should().NotContain("RelatedService");
        result.Should().NotContain("PaymentFlowTests");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 1 suppressed noise node");
    }

    [Fact]
    public async Task FindSimilarToNodeAsync_WhenBroaderOutputEnabled_ShowsCrossFamilyAndSuppressedMatches()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                IncludeBroaderHeuristicMatches = true,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindSimilarToNodeAsync("Method:Shop.Application.Foo.Bar", null, 10, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("s1", "SimilarMethod", CodeNodeType.Method, "src/Application/Similar.cs", project: "Shop", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 0.96),
                 (Node("s2", "RelatedService", CodeNodeType.Class, "src/Application/RelatedService.cs", project: "Shop", fileRole: IndexedFileRole.Source, @namespace: "Shop.Application.Payments"), 0.93),
                 (Node("s3", "PaymentFlowTests", CodeNodeType.Method, "tests/Application/PaymentFlowTests.cs", project: "Shop", fileRole: IndexedFileRole.Test, @namespace: "Shop.Tests.Payments"), 0.97)
             ]);

        var result = await sut.FindSimilarToNodeAsync("Method:Shop.Application.Foo.Bar");

        result.Should().Contain("### Same-family production matches (1)");
        result.Should().Contain("### Broader semantic matches (1)");
        result.Should().Contain("### Suppressed test/config matches (1)");
        result.Should().Contain("RelatedService");
        result.Should().Contain("PaymentFlowTests");
    }

}

