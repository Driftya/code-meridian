using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryGetSubgraphSummaryIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task GetSubgraphSummaryAsync_ForKnownNode_ReturnsReadableSummary()
    {
        var target = await FindNodeWithRelationshipsAsync();
        target.Should().NotBeNull("the test seeds an isolated baseline graph with relationships");

        var summary = await _repository!.GetSubgraphSummaryAsync(target!.Id);

        summary.Should().NotBeNullOrWhiteSpace();
        summary.Should().Contain(target.Name);
        (summary.Contains("Relations", StringComparison.OrdinalIgnoreCase) || summary.Contains("File:", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }


}
