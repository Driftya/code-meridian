using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindArchitectureViolationsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindArchitectureViolationsAsync_WithIndexedArchitectureConfig_UsesConfiguredLayers()
    {
        var projectContext = $"Integration.ArchitectureConfig.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Domain.OrderRules",
            name: "OrderRules",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Domain/OrderRules.cs",
            namespaceName: $"{projectContext}.Domain");
        var target = CreateNode(
            id: $"{projectContext}.Persistence.SqlOrderStore",
            name: "SqlOrderStore",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Persistence/SqlOrderStore.cs",
            namespaceName: $"{projectContext}.Persistence");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(target);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await IngestArchitectureConfigAsync(
                projectContext,
                ".meridian/architecture.json",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = "Custom Onion",
                    ["layers:0:id"] = "Domain",
                    ["layers:0:namespaceContainsAny:0"] = ".Domain",
                    ["layers:1:id"] = "Persistence",
                    ["layers:1:namespaceContainsAny:0"] = ".Persistence",
                    ["forbiddenDependencies:0:from"] = "Domain",
                    ["forbiddenDependencies:0:to"] = "Persistence",
                    ["forbiddenDependencies:0:reason"] = "Domain must not depend on Persistence"
                });

            var violations = await _repository.FindArchitectureViolationsAsync(projectContext);

            violations.Should().ContainSingle(item =>
                item.Source.Id == source.Id
                && item.Target.Id == target.Id
                && item.Violation == "Domain must not depend on Persistence");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
