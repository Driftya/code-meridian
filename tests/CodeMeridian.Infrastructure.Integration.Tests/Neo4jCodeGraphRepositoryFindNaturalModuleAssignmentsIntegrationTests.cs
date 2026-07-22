using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindNaturalModuleAssignmentsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindNaturalModuleAssignmentsAsync_WithTemporaryFixture_ReturnsRequestedCommunities()
    {
        var projectContext = $"Integration.NaturalModules.Assignments.{Guid.NewGuid():N}";
        var service = CreateNode(
            id: $"{projectContext}.OrderService",
            name: "OrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderService.cs",
            namespaceName: $"{projectContext}.Application.Orders");
        var place = CreateNode(
            id: $"{projectContext}.OrderService.PlaceOrderAsync",
            name: "PlaceOrderAsync",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: service.FilePath!,
            namespaceName: service.Namespace);
        var price = CreateNode(
            id: $"{projectContext}.OrderService.CalculatePriceAsync",
            name: "CalculatePriceAsync",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: service.FilePath!,
            namespaceName: service.Namespace);
        var repository = CreateNode(
            id: $"{projectContext}.IOrderRepository",
            name: "IOrderRepository",
            type: CodeNodeType.Interface,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderRepository.cs",
            namespaceName: $"{projectContext}.Application.Orders");
        var endpoint = CreateNode(
            id: $"{projectContext}.OrdersEndpoint",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var other = CreateNode(
            id: $"{projectContext}.EmailSender",
            name: "EmailSender",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/EmailSender.cs",
            namespaceName: $"{projectContext}.Application.Notifications");

        try
        {
            await _repository!.UpsertNodeAsync(service);
            await _repository.UpsertNodeAsync(place);
            await _repository.UpsertNodeAsync(price);
            await _repository.UpsertNodeAsync(repository);
            await _repository.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(other);

            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = service.Id, TargetId = place.Id, Type = CodeEdgeType.Contains });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = service.Id, TargetId = price.Id, Type = CodeEdgeType.Contains });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = endpoint.Id, TargetId = place.Id, Type = CodeEdgeType.Calls });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = endpoint.Id, TargetId = price.Id, Type = CodeEdgeType.Calls });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = place.Id, TargetId = repository.Id, Type = CodeEdgeType.DependsOn });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = price.Id, TargetId = repository.Id, Type = CodeEdgeType.DependsOn });

            var assignments = await _repository.FindNaturalModuleAssignmentsAsync(
                [place.Id, price.Id, repository.Id, endpoint.Id, other.Id],
                projectContext);
            var byId = assignments.ToDictionary(item => item.Node.Id, item => item.Community, StringComparer.Ordinal);

            byId.Should().ContainKey(place.Id);
            byId.Should().ContainKey(price.Id);
            byId.Should().ContainKey(repository.Id);
            byId.Should().ContainKey(endpoint.Id);
            byId[place.Id].Should().Be(byId[price.Id]);
            byId[place.Id].Should().Be(byId[repository.Id]);
            byId[place.Id].Should().Be(byId[endpoint.Id]);
            byId[other.Id].Should().NotBe(byId[place.Id]);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
