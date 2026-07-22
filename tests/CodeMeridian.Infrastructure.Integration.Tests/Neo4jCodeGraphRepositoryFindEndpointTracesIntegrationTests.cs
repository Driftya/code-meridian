using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindEndpointTracesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindEndpointTracesAsync_WithDatabaseAndMessageFixture_ReturnsTerminalPaths()
    {
        var projectContext = $"Integration.TraceEndpoint.{Guid.NewGuid():N}";
        var endpoint = CreateNode(
            id: $"{projectContext}.Endpoint",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs");
        var handler = CreateNode(
            id: $"{projectContext}.Handler",
            name: "CreateOrder",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var operation = CreateNode(
            id: $"{projectContext}.DbOperation",
            name: "EFCore Writes Orders",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersRepository.cs",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = "EFCore"
            });
        var table = CreateNode(
            id: $"{projectContext}.OrdersTable",
            name: "Orders",
            type: CodeNodeType.DatabaseTable,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseTable"
            });
        var topic = CreateNode(
            id: $"{projectContext}.OrderCreated",
            name: "order-created",
            type: CodeNodeType.MessageTopic,
            projectContext: projectContext,
            filePath: string.Empty);

        try
        {
            await _repository!.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(handler);
            await _repository.UpsertNodeAsync(operation);
            await _repository.UpsertNodeAsync(table);
            await _repository.UpsertNodeAsync(topic);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = endpoint.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = operation.Id,
                Type = CodeEdgeType.Writes
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = operation.Id,
                TargetId = table.Id,
                Type = CodeEdgeType.Writes
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = topic.Id,
                Type = CodeEdgeType.PublishesTo
            });

            var traces = await _repository.FindEndpointTracesAsync("POST /api/orders", projectContext);

            traces.Select(path => path.Steps.Last().Node.Id).Should().Contain(table.Id);
            traces.Select(path => path.Steps.Last().Node.Id).Should().Contain(topic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
