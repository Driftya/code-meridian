using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindHybridMatchesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindHybridMatchesAsync_WithAnchorAndEmbedding_ReturnsNearbySemanticMatches()
    {
        var projectContext = $"Integration.Hybrid.{Guid.NewGuid():N}";
        var anchor = CreateNode(
            id: $"{projectContext}.OrderService",
            name: "OrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderService.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.1f, 0.2f, 0.3f, 0.4f]);
        var nearby = CreateNode(
            id: $"{projectContext}.RetryPolicy",
            name: "RetryPolicy",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/RetryPolicy.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.11f, 0.19f, 0.29f, 0.39f]);
        var distant = CreateNode(
            id: $"{projectContext}.OutsideScope",
            name: "OutsideScope",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OutsideScope.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.11f, 0.19f, 0.29f, 0.39f]);

        try
        {
            await _repository!.UpsertNodeAsync(anchor);
            await _repository.UpsertNodeAsync(nearby);
            await _repository.UpsertNodeAsync(distant);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = anchor.Id,
                TargetId = nearby.Id,
                Type = CodeEdgeType.Calls
            });

            var results = await _repository.FindHybridMatchesAsync(
                [0.1f, 0.2f, 0.3f, 0.4f],
                nearNodeId: anchor.Id,
                maxHops: 2,
                projectContext: projectContext,
                excludeTests: true,
                topK: 10);

            results.Should().ContainSingle(match => match.Node.Id == nearby.Id);
            results.Should().NotContain(match => match.Node.Id == distant.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
