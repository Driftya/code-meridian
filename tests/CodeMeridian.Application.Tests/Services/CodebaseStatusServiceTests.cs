using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseStatusServiceTests
{
    private static (CodebaseStatusService Sut, ICodeGraphRepository Graph, IVectorRepository Vector, IEmbeddingProvider Embeddings, ICodebaseQueryService Query) Build()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var embeddings = Substitute.For<IEmbeddingProvider>();
        embeddings.ProviderName.Returns("Ollama");
        embeddings.Dimensions.Returns(768);
        var query = Substitute.For<ICodebaseQueryService>();
        return (new CodebaseStatusService(graph, vector, embeddings, query), graph, vector, embeddings, query);
    }

    [Fact]
    public async Task GetDoctorStatusAsync_WithHealthyBackend_ReturnsCountsAndDrift()
    {
        var (sut, graph, vector, embeddings, query) = Build();

        graph.CountCodeNodesAsync("Shop", Arg.Any<CancellationToken>()).Returns(12_482);
        graph.CountCallEdgesAsync("Shop", Arg.Any<CancellationToken>()).Returns(34_901);
        graph.CountDiagnosticsAsync("Shop", Arg.Any<CancellationToken>()).Returns(14);
        vector.CountAsync("Shop", Arg.Any<CancellationToken>()).Returns(78);
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);
        query.FindGraphDriftAsync("Shop", 10, Arg.Any<CancellationToken>()).Returns("Graph drift: low");

        var result = await sut.GetDoctorStatusAsync("Shop");

        result.Neo4jReachable.Should().BeTrue();
        result.IndexedNodes.Should().Be(12_482);
        result.CallEdges.Should().Be(34_901);
        result.DocumentsIndexed.Should().Be(78);
        result.DiagnosticsIndexed.Should().Be(14);
        result.GraphDrift.Should().Be("low");
        result.GraphDriftReport.Should().Be("Graph drift: low");
        result.EmbeddingsEnabled.Should().BeFalse();
        result.EmbeddingProvider.Should().Be(embeddings.ProviderName);
        result.EmbeddingDimensions.Should().Be(embeddings.Dimensions);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetDoctorStatusAsync_WhenNeo4jFails_ReturnsDegradedStatus()
    {
        var (sut, graph, vector, embeddings, query) = Build();

        graph.CountCodeNodesAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new InvalidOperationException("Neo4j unavailable")));
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        query.FindGraphDriftAsync(null, 10, Arg.Any<CancellationToken>()).Returns("Graph drift: high");

        var result = await sut.GetDoctorStatusAsync();

        result.Neo4jReachable.Should().BeFalse();
        result.GraphDrift.Should().Be("high");
        result.GraphDriftReport.Should().Contain("Neo4j unavailable");
        result.EmbeddingsEnabled.Should().BeTrue();
        result.Error.Should().Contain("Neo4j unavailable");
    }
}
