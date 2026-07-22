using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryRiskCoreGdsQueriesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task RiskCoreGdsQueries_UseSharedStructuralRelationships()
    {
        var projectContext = $"Integration.RiskyCore.{Guid.NewGuid():N}";
        var entry = CreateNode(
            id: $"{projectContext}.OrdersPage",
            name: "OrdersPage.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersPage.tsx",
            namespaceName: $"{projectContext}.Frontend");
        var endpoint = CreateNode(
            id: $"{projectContext}.PostOrders",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var service = CreateNode(
            id: $"{projectContext}.CreateOrderService",
            name: "CreateOrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CreateOrderService.cs",
            namespaceName: $"{projectContext}.Application");
        var store = CreateNode(
            id: $"{projectContext}.OrdersTable",
            name: "Orders",
            type: CodeNodeType.DatabaseTable,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Orders.sql",
            namespaceName: $"{projectContext}.Infrastructure");

        try
        {
            await _repository!.UpsertNodeAsync(entry);
            await _repository.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(service);
            await _repository.UpsertNodeAsync(store);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = entry.Id,
                TargetId = endpoint.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = endpoint.Id,
                TargetId = service.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = service.Id,
                TargetId = store.Id,
                Type = CodeEdgeType.Writes
            });

            var pageRank = await _repository.GetPageRankAsync(projectContext, limit: 10);
            var betweenness = await _repository.GetBetweennessAsync(projectContext, limit: 10);
            var articulation = await _repository.GetArticulationPointsAsync(projectContext, limit: 10);
            var bridgeEdges = await _repository.GetBridgeEdgesAsync(projectContext, limit: 10);

            pageRank.Should().NotBeEmpty();
            betweenness.Should().Contain(item => item.Node.Id == endpoint.Id || item.Node.Id == service.Id);
            articulation.Should().Contain(item => item.Node.Id == endpoint.Id || item.Node.Id == service.Id);
            bridgeEdges.Should().Contain(edge =>
                (edge.Source.Id == entry.Id && edge.Target.Id == endpoint.Id)
                || (edge.Source.Id == endpoint.Id && edge.Target.Id == service.Id)
                || (edge.Source.Id == service.Id && edge.Target.Id == store.Id));
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
