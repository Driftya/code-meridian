using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindSmellPathsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindSmellPathsAsync_WithCoreToInfrastructurePath_ReturnsShortestPath()
    {
        var projectContext = $"Integration.SmellPaths.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Core.PricingRules",
            name: "PricingRules",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Core/PricingRules.cs",
            namespaceName: $"{projectContext}.Core");
        var middle = CreateNode(
            id: $"{projectContext}.Application.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Application/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Application");
        var target = CreateNode(
            id: $"{projectContext}.Infrastructure.Neo4jOrderStore",
            name: "Neo4jOrderStore",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Infrastructure/Neo4jOrderStore.cs",
            namespaceName: $"{projectContext}.Infrastructure");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(middle);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = middle.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = middle.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindSmellPathsAsync(projectContext, maxDepth: 4);

            results.Should().ContainSingle(path =>
                path.Source.Id == source.Id
                && path.Target.Id == target.Id
                && path.Distance == 2
                && path.Violation.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                && path.Steps.Count == 3
                && path.Steps[0].Node.Id == source.Id
                && path.Steps[1].Node.Id == middle.Id
                && path.Steps[2].Node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
