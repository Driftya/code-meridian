using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class Neo4jCodeGraphRepositoryQueryContractTests
{
    [Fact]
    public async Task StructuralQueries_WithNoMatches_ReturnEmptyResults()
    {
        var repository = CreateRepository();

        await AssertEmpty(repository.FindCrossProjectDependenciesAsync("CodeMeridian"));
        await AssertEmpty(repository.FindCoverageGapsAsync("CodeMeridian"));
        await AssertEmpty(repository.FindRelatedTestsAsync("node-id", "CodeMeridian"));
        await AssertEmpty(repository.FindRecentlyChangedAsync("CodeMeridian", TimeSpan.FromDays(7)));
        await AssertEmpty(repository.FindImpactAsync("node-id", 99));
        await AssertEmpty(repository.FindImpactPathsAsync("node-id", 0));
        await AssertEmpty(repository.FindDownstreamAsync("node-id", 3));
        await AssertEmpty(repository.FindHotspotsAsync("CodeMeridian", 5));
        await AssertEmpty(repository.FindConnectionAsync("from-id", "to-id"));
        await AssertEmpty(repository.FindUnreferencedAsync("CodeMeridian"));
        await AssertEmpty(repository.FindLargeNodesAsync("CodeMeridian", 200, 20));
        await AssertEmpty(repository.FindGodClassesAsync("CodeMeridian", 200, 2));
        await AssertEmpty(repository.FindCyclesAsync("CodeMeridian"));
        await AssertEmpty(repository.FindArchitectureViolationsAsync("CodeMeridian"));
        await AssertEmpty(repository.FindSmellPathsAsync("CodeMeridian", 99));
        await AssertEmpty(repository.FindHighChurnAsync("CodeMeridian", 2));
        await AssertEmpty(repository.FindEndpointTracesAsync("GET /api/items", "CodeMeridian", 99));
    }

    [Fact]
    public async Task ConfigurationQueries_WithNoMatches_ReturnEmptyResults()
    {
        var repository = CreateRepository();

        await AssertEmpty(repository.FindConfigDefinitionsAsync("Logging:Level", "CodeMeridian"));
        await AssertEmpty(repository.FindConfigUsageAsync("Logging:Level", "CodeMeridian"));
        await AssertEmpty(repository.FindConfigUsageAsync("Logging", null));
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithNoMatchingNode_ReturnsEmptyContext()
    {
        var repository = CreateRepository();

        var result = await repository.GetContextForEditingAsync("missing-node");

        result.Node.Should().BeNull();
        result.Callers.Should().BeEmpty();
        result.Callees.Should().BeEmpty();
        result.Interfaces.Should().BeEmpty();
    }

    private static Neo4jCodeGraphRepository CreateRepository()
    {
        var harness = new Neo4jRepositoryTestHarness();
        return new Neo4jCodeGraphRepository(
            harness.Driver,
            NullLogger<Neo4jCodeGraphRepository>.Instance);
    }

    private static async Task AssertEmpty<T>(Task<IReadOnlyList<T>> operation)
    {
        var result = await operation;
        result.Should().BeEmpty();
    }
}
