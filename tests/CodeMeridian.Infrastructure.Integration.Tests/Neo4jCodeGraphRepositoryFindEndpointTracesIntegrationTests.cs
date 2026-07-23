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

    [Fact]
    public async Task FindEndpointTracesAsync_DoesNotCrossContainsToUnrelatedImplementationMember()
    {
        var projectContext = $"Integration.TraceEndpoint.Siblings.{Guid.NewGuid():N}";
        CodeNode Node(string suffix, string name, CodeNodeType type, string file) => CreateNode(
            id: $"{projectContext}.{suffix}",
            name: name,
            type: type,
            projectContext: projectContext,
            filePath: file,
            namespaceName: $"{projectContext}.Data");

        var endpoint = Node("Endpoint", "DELETE /api/diagnostics", CodeNodeType.ApiEndpoint, "src/Api.cs");
        var handler = Node("Handler", "DeleteDiagnosticsAsync()", CodeNodeType.Method, "src/Api.cs");
        var contract = Node("Contract", "DeleteDiagnosticsAsync()", CodeNodeType.Method, "src/IRepository.cs");
        var interfaceType = Node("Interface", "IRepository", CodeNodeType.Interface, "src/IRepository.cs");
        var implementationType = Node("Implementation", "Repository", CodeNodeType.Class, "src/Repository.cs");
        var implementation = Node("ImplementationMethod", "DeleteDiagnosticsAsync()", CodeNodeType.Method, "src/Repository.cs");
        var unrelated = Node("UnrelatedMethod", "LoadAnalysisAsync()", CodeNodeType.Method, "src/Repository.cs");
        var ownedOperation = Node("OwnedOperation", "Neo4j Writes Diagnostics", CodeNodeType.ExternalConcept, "src/Repository.cs") with
        {
            Properties = new Dictionary<string, string> { ["externalKind"] = "DatabaseOperation", ["recognitionConfidence"] = "0.96" }
        };
        var unrelatedOperation = Node("UnrelatedOperation", "Neo4j Reads Calls", CodeNodeType.ExternalConcept, "src/Repository.cs") with
        {
            Properties = new Dictionary<string, string> { ["externalKind"] = "DatabaseOperation", ["recognitionConfidence"] = "0.96" }
        };
        var diagnosticsTable = Node("Diagnostics", "Diagnostics", CodeNodeType.DatabaseTable, string.Empty);
        var callsTable = Node("Calls", "Calls", CodeNodeType.DatabaseTable, string.Empty);

        var nodes = new[]
        {
            endpoint, handler, contract, interfaceType, implementationType, implementation, unrelated,
            ownedOperation, unrelatedOperation, diagnosticsTable, callsTable
        };

        try
        {
            foreach (var node in nodes)
                await _repository!.UpsertNodeAsync(node);

            var edges = new[]
            {
                new CodeEdge { SourceId = handler.Id, TargetId = endpoint.Id, Type = CodeEdgeType.Uses },
                new CodeEdge { SourceId = handler.Id, TargetId = contract.Id, Type = CodeEdgeType.Calls },
                new CodeEdge { SourceId = interfaceType.Id, TargetId = contract.Id, Type = CodeEdgeType.Contains },
                new CodeEdge { SourceId = implementationType.Id, TargetId = implementation.Id, Type = CodeEdgeType.Contains },
                new CodeEdge { SourceId = implementationType.Id, TargetId = unrelated.Id, Type = CodeEdgeType.Contains },
                new CodeEdge { SourceId = implementation.Id, TargetId = contract.Id, Type = CodeEdgeType.Implements },
                new CodeEdge { SourceId = implementation.Id, TargetId = ownedOperation.Id, Type = CodeEdgeType.Writes },
                new CodeEdge { SourceId = ownedOperation.Id, TargetId = diagnosticsTable.Id, Type = CodeEdgeType.Writes },
                new CodeEdge { SourceId = unrelated.Id, TargetId = unrelatedOperation.Id, Type = CodeEdgeType.Reads },
                new CodeEdge { SourceId = unrelatedOperation.Id, TargetId = callsTable.Id, Type = CodeEdgeType.Reads }
            };
            foreach (var edge in edges)
                await _repository!.UpsertEdgeAsync(edge);

            var traces = await _repository!.FindEndpointTracesAsync(endpoint.Name, projectContext);
            var terminals = traces.Select(path => path.Steps.Last().Node.Id).ToArray();

            terminals.Should().Contain(diagnosticsTable.Id);
            terminals.Should().NotContain(callsTable.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
