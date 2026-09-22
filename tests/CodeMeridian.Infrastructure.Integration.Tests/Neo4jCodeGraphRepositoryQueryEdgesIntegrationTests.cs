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

    [Fact]
    public async Task UpsertEdgeAsync_EvidenceRoundTripsAndUpdatesExistingRelationship()
    {
        var legacy = (await _repository!.QueryEdgesAsync(BaselineMethod.Id, 1))
            .Where(edge => edge.Type == CodeEdgeType.Calls).ToArray();
        legacy.Should().ContainSingle();
        legacy[0].EvidenceKind.Should().Be(EdgeEvidenceKind.Unknown);
        (await _repository.CountEdgeEvidenceAsync(BaselineProjectContext))[EdgeEvidenceKind.Unknown].Should().Be(2);

        var edge = new CodeEdge
        {
            SourceId = BaselineMethod.Id,
            TargetId = BaselineDependency.Id,
            Type = CodeEdgeType.Calls,
            EvidenceKind = EdgeEvidenceKind.Extracted,
            EvidenceReason = "roslyn_symbol",
            Resolver = "roslyn.semantic",
            SourceFilePath = BaselineMethod.FilePath,
            SourceLine = 8,
            SourceColumn = 4,
            SourceEndLine = 8,
            SourceEndColumn = 19,
            EvidenceDetails = new() { ["receiverType"] = "FixtureDependency" }
        };
        await _repository.UpsertEdgeAsync(edge);
        var extracted = (await _repository.QueryEdgesAsync(BaselineMethod.Id, 1))
            .Where(item => item.Type == CodeEdgeType.Calls).ToArray();
        extracted.Should().ContainSingle();
        var extractedCounts = await _repository.CountEdgeEvidenceAsync(BaselineProjectContext);
        extractedCounts[EdgeEvidenceKind.Extracted].Should().Be(1);
        extractedCounts[EdgeEvidenceKind.Unknown].Should().Be(1);
        extracted[0].Should().BeEquivalentTo(edge, options => options.Excluding(item => item.Id)
            .Excluding(item => item.SourceId).Excluding(item => item.TargetId));

        await _repository.UpsertEdgeAsync(edge with
        {
            EvidenceKind = EdgeEvidenceKind.Inferred,
            EvidenceReason = "syntax_fallback",
            Resolver = "roslyn.syntax",
            EvidenceDetails = null
        });
        var updated = (await _repository.QueryEdgesAsync(BaselineMethod.Id, 1))
            .Where(item => item.Type == CodeEdgeType.Calls).ToArray();
        updated.Should().ContainSingle();
        updated[0].EvidenceKind.Should().Be(EdgeEvidenceKind.Inferred);
        updated[0].EvidenceDetails.Should().BeNull();
    }


}
