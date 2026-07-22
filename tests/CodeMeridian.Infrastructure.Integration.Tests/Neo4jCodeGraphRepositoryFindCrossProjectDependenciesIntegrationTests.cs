using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindCrossProjectDependenciesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindCrossProjectDependenciesAsync_WithTemporaryFixtures_ReturnsCrossProjectEdge()
    {
        var sourceProject = $"Integration.CrossProject.Source.{Guid.NewGuid():N}";
        var targetProject = $"Integration.CrossProject.Target.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{sourceProject}.Source",
            name: "SourceMethod",
            type: CodeNodeType.Method,
            projectContext: sourceProject,
            filePath: $"src/{sourceProject}/Source.cs",
            namespaceName: $"{sourceProject}.Production");
        var target = CreateNode(
            id: $"{targetProject}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: targetProject,
            filePath: $"src/{targetProject}/Target.cs",
            namespaceName: $"{targetProject}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var dependencies = await _repository.FindCrossProjectDependenciesAsync();

            dependencies.Should().ContainSingle(item =>
                item.Source.Id == source.Id
                && item.Target.Id == target.Id
                && item.RelationshipType == "Calls");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(sourceProject);
            await _repository.DeleteProjectAsync(targetProject);
        }
    }


}
