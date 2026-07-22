using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindHybridSearchTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindHybridSearchAsync_WithResults_UsesEmbeddingAndGraphConstraints()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        embeddings.GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>())
            .Returns(expectedEmbedding);
        graph.FindHybridMatchesAsync(
                Arg.Is<float[]>(embedding => embedding.SequenceEqual(expectedEmbedding)),
                "OrderService",
                3,
                "Shop",
                true,
                10,
                Arg.Any<CancellationToken>())
            .Returns([
                (Node("n1", "RetryPolicy", CodeNodeType.Class, "src/RetryPolicy.cs"), 0.94),
                (Node("n2", "BackoffHelper", CodeNodeType.Method, "src/BackoffHelper.cs"), 0.82)
            ]);

        var result = await sut.FindHybridSearchAsync("retry policy", nearNodeId: "OrderService", projectContext: "Shop");

        result.Should().Contain("## Hybrid Semantic Graph Search");
        result.Should().Contain("retry policy");
        result.Should().Contain("OrderService");
        result.Should().Contain("RetryPolicy");
        result.Should().Contain("94.0%");
        result.Should().Contain("BackoffHelper");
        await embeddings.Received(1).GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>());
        await graph.Received(1).FindHybridMatchesAsync(Arg.Any<float[]>(), "OrderService", 3, "Shop", true, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindHybridSearchAsync_WithMissingFilePath_FormatsAsciiFallbackOutput()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f, 0.3f]);
        graph.FindHybridMatchesAsync(
                Arg.Any<float[]>(),
                null,
                3,
                "Shop",
                true,
                10,
                Arg.Any<CancellationToken>())
            .Returns([
                (Node("n1", "RetryPolicy", CodeNodeType.Class, null, null, "Shop"), 0.82)
            ]);

        var result = await sut.FindHybridSearchAsync("retry policy", projectContext: "Shop");

        result.Should().Contain("## Hybrid Semantic Graph Search - `retry policy`");
        result.Should().Contain("| 82.0% | Class | `RetryPolicy` | - |");
        result.Should().NotContain("\u00C3\u00A2");
    }

    // ── FindDuplicateCandidatesAsync ─────────────────────────────────────────

    [Fact]
    public async Task FindHybridSearchAsync_WithAnchorAndNoNearbyMatches_ReturnsGuidanceInsteadOfEmbeddingFailure()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("tool dependency impact", Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f, 0.3f]);
        graph.FindHybridMatchesAsync(
                Arg.Any<float[]>(),
                "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::FindStaleKnowledgeAsync(string?,int,CancellationToken)",
                3,
                "CodeMeridian",
                true,
                5,
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindHybridSearchAsync(
            "tool dependency impact",
            nearNodeId: "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::FindStaleKnowledgeAsync(string?,int,CancellationToken)",
            projectContext: "CodeMeridian",
            limit: 5);

        result.Should().Be("No hybrid-search results found. Try broadening the graph neighborhood, lowering filters, or indexing more embedded nodes.");
        result.Should().NotContain("requires embeddings to be enabled");
    }


}

