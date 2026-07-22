using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryQueryEdgesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task QueryEdgesAsync_ForKnownNode_ReturnsRelationships()
    {
        var target = await FindNodeWithRelationshipsAsync();
        target.Should().NotBeNull("the test seeds an isolated baseline graph with relationships");

        var edges = await _repository!.QueryEdgesAsync(target!.Id, depth: 1);

        edges.Should().NotBeEmpty();
        edges.Should().OnlyContain(edge =>
            !string.IsNullOrWhiteSpace(edge.SourceId)
            && !string.IsNullOrWhiteSpace(edge.TargetId));
    }


}
