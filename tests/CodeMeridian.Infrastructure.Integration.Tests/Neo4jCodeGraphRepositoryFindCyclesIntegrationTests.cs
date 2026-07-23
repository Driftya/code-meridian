using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindCyclesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindCyclesAsync_ReturnsCanonicalProductionPairAndExcludesTestOnlyCoupling()
    {
        var projectContext = $"Integration.Cycles.{Guid.NewGuid():N}";
        CodeNode Node(string id, string namespaceName, IndexedFileRole role) => CreateNode(
            id: $"{projectContext}.{id}",
            name: id,
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: role == IndexedFileRole.Test ? $"tests/{id}.cs" : $"src/{id}.cs",
            namespaceName: namespaceName) with { FileRole = role };

        var alpha = Node("Alpha", $"{projectContext}.Alpha", IndexedFileRole.Source);
        var beta = Node("Beta", $"{projectContext}.Beta", IndexedFileRole.Source);
        var testLeft = Node("TestLeft", $"{projectContext}.TestLeft", IndexedFileRole.Test);
        var testRight = Node("TestRight", $"{projectContext}.TestRight", IndexedFileRole.Test);

        try
        {
            foreach (var node in new[] { alpha, beta, testLeft, testRight })
                await _repository!.UpsertNodeAsync(node);
            foreach (var (source, target) in new[] { (alpha, beta), (beta, alpha), (testLeft, testRight), (testRight, testLeft) })
                await _repository!.UpsertEdgeAsync(new CodeEdge { SourceId = source.Id, TargetId = target.Id, Type = CodeEdgeType.Calls });

            var cycles = await _repository!.FindCyclesAsync(projectContext);

            cycles.Should().ContainSingle()
                .Which.Should().Be(($"{projectContext}.Alpha", $"{projectContext}.Beta"));
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }
}
