using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class Neo4jCodeGraphRepositoryGdsQueryContractTests
{
    [Fact]
    public async Task GdsQueries_WithNoStructuralRelationships_ReturnEmptyResults()
    {
        var harness = new Neo4jRepositoryTestHarness();
        var repository = new Neo4jCodeGraphRepository(
            harness.Driver,
            NullLogger<Neo4jCodeGraphRepository>.Instance);

        await AssertEmpty(repository.GetPageRankAsync("CodeMeridian", 5));
        await AssertEmpty(repository.GetBetweennessAsync("CodeMeridian", 5));
        await AssertEmpty(repository.GetArticulationPointsAsync("CodeMeridian", 5));
        await AssertEmpty(repository.GetBridgeEdgesAsync("CodeMeridian", 5));
        await AssertEmpty(repository.FindNaturalModulesAsync("CodeMeridian"));
        await AssertEmpty(repository.FindNaturalModuleAssignmentsAsync(["one", "two"], "CodeMeridian"));
        await AssertEmpty(repository.FindNaturalModuleAssignmentsAsync([], null));
        await AssertEmpty(repository.FindSimilarToNodeAsync("node-id", "CodeMeridian", 5));
        await AssertEmpty(repository.FindHybridMatchesAsync([0.1f, 0.2f], "near-id", 99, "CodeMeridian", true, 5));
        await AssertEmpty(repository.FindHybridMatchesAsync([0.1f], null, 0, null, false, 5));
        await AssertEmpty(repository.FindImplementationPatternCandidatesAsync([0.1f], "CodeMeridian", true, 5));
        await AssertEmpty(repository.FindDuplicateCandidatesAsync("CodeMeridian", "Services", null, 3, 0.8, true, 5));
    }

    private static async Task AssertEmpty<T>(Task<IReadOnlyList<T>> operation)
    {
        var result = await operation;
        result.Should().BeEmpty();
    }
}
