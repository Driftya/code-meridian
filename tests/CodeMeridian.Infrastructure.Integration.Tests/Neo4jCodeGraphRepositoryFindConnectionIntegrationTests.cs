using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindConnectionIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindConnectionAsync_WithTemporaryFixture_ReturnsShortestPath()
    {
        var projectContext = $"Integration.Connection.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Source",
            name: "SourceMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Source.cs",
            namespaceName: $"{projectContext}.Production");
        var middle = CreateNode(
            id: $"{projectContext}.Middle",
            name: "MiddleMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Middle.cs",
            namespaceName: $"{projectContext}.Production");
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(middle);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = middle.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = middle.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var connection = await _repository.FindConnectionAsync(source.Id, target.Id);

            connection.Should().HaveCount(3);
            connection[0].Node.Id.Should().Be(source.Id);
            connection[0].ViaRelationship.Should().Be("Calls");
            connection[1].Node.Id.Should().Be(middle.Id);
            connection[1].ViaRelationship.Should().Be("Calls");
            connection[2].Node.Id.Should().Be(target.Id);
            connection[2].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_UnrelatedSiblingMethods_AreNotConnectedOnlyByContainment()
    {
        var projectContext = $"Integration.Connection.Siblings.{Guid.NewGuid():N}";
        var container = CreateNode($"{projectContext}.Container", "Fixture", CodeNodeType.Class, projectContext, $"tests/{projectContext}/Fixture.cs");
        var first = CreateNode($"{projectContext}.First", "FirstTest", CodeNodeType.Method, projectContext, container.FilePath);
        var second = CreateNode($"{projectContext}.Second", "SecondTest", CodeNodeType.Method, projectContext, container.FilePath);

        try
        {
            await _repository!.UpsertNodeAsync(container);
            await _repository.UpsertNodeAsync(first);
            await _repository.UpsertNodeAsync(second);
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = container.Id, TargetId = first.Id, Type = CodeEdgeType.Contains });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = container.Id, TargetId = second.Id, Type = CodeEdgeType.Contains });

            var connection = await _repository.FindConnectionAsync(first.Id, second.Id);

            connection.Should().BeEmpty();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_WithFrontendFixture_ReturnsFrontendRelationshipPath()
    {
        var projectContext = $"Integration.Connection.Frontend.{Guid.NewGuid():N}";
        var component = CreateNode(
            id: $"{projectContext}.Component",
            name: "HeroCard.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.tsx",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "ComponentFile"
            });
        var classConcept = CreateNode(
            id: $"{projectContext}.CssClass.hero",
            name: "hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
            });
        var selector = CreateNode(
            id: $"{projectContext}.CssSelector.hero",
            name: ".hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            });
        var stylesheet = CreateNode(
            id: $"{projectContext}.Stylesheet",
            name: "HeroCard.scss",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.scss",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "StyleSheetFile"
            });

        try
        {
            await _repository!.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(classConcept);
            await _repository.UpsertNodeAsync(selector);
            await _repository.UpsertNodeAsync(stylesheet);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = selector.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var connection = await _repository.FindConnectionAsync(component.Id, stylesheet.Id);

            connection.Should().HaveCount(4);
            connection[0].Node.Id.Should().Be(component.Id);
            connection[0].ViaRelationship.Should().Be("UsesClass");
            connection[1].Node.Id.Should().Be(classConcept.Id);
            connection[1].ViaRelationship.Should().Be("UsesClass");
            connection[2].Node.Id.Should().Be(selector.Id);
            connection[2].ViaRelationship.Should().Be("DefinesSelector");
            connection[3].Node.Id.Should().Be(stylesheet.Id);
            connection[3].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_WithDatabaseFixture_ReturnsDatabaseOperationAndTablePath()
    {
        var projectContext = $"Integration.Connection.Database.{Guid.NewGuid():N}";
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

        try
        {
            await _repository!.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(handler);
            await _repository.UpsertNodeAsync(operation);
            await _repository.UpsertNodeAsync(table);

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

            var connection = await _repository.FindConnectionAsync(endpoint.Id, table.Id);

            connection.Should().HaveCount(4);
            connection[0].Node.Id.Should().Be(endpoint.Id);
            connection[0].ViaRelationship.Should().Be("Uses");
            connection[1].Node.Id.Should().Be(handler.Id);
            connection[1].ViaRelationship.Should().Be("Writes");
            connection[2].Node.Id.Should().Be(operation.Id);
            connection[2].ViaRelationship.Should().Be("Writes");
            connection[3].Node.Id.Should().Be(table.Id);
            connection[3].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
