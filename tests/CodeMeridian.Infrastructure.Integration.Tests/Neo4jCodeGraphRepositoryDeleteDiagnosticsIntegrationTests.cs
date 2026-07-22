using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryDeleteDiagnosticsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task DeleteDiagnosticsAsync_PreservesCompatibleIndexRunMetadata()
    {
        var projectContext = $"Integration.DeleteDiagnostics.{Guid.NewGuid():N}";
        var diagnostic = CreateNode(
            id: $"{projectContext}::Diagnostic::compiler-error",
            name: "error CS0001",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: "src/Broken.cs");
        var indexRun = CreateNode(
            id: $"{projectContext}::IndexRun::incremental",
            name: "incremental C# index run",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: "metadata/index-run",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "IndexRun",
                ["mode"] = "incremental",
                ["attemptedCallEdges"] = "10",
                ["resolvedCallEdges"] = "10"
            });

        try
        {
            await _repository!.UpsertNodeAsync(diagnostic);
            await _repository.UpsertNodeAsync(indexRun);

            await _repository.DeleteDiagnosticsAsync(projectContext);

            var remaining = await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 10
            });

            remaining.Should().ContainSingle(node => node.Id == indexRun.Id);
            remaining.Should().NotContain(node => node.Id == diagnostic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }
}
