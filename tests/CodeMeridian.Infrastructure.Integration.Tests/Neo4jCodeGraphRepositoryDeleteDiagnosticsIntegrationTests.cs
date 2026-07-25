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
        var otherProjectContext = $"{projectContext}.Other";
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
        var otherProjectDiagnostic = CreateNode(
            id: $"{otherProjectContext}::Diagnostic::keep",
            name: "warning CS9999",
            type: CodeNodeType.Diagnostic,
            projectContext: otherProjectContext,
            filePath: "src/Other.cs");

        try
        {
            await _repository!.UpsertNodeAsync(diagnostic);
            await _repository.UpsertNodeAsync(indexRun);
            await _repository.UpsertNodeAsync(otherProjectDiagnostic);

            var deletedCount = await _repository.DeleteDiagnosticsAsync(projectContext);

            var remaining = await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 10
            });

            deletedCount.Should().Be(1);
            remaining.Should().ContainSingle(node => node.Id == indexRun.Id);
            remaining.Should().NotContain(node => node.Id == diagnostic.Id);
            (await _repository.CountDiagnosticsAsync(projectContext)).Should().Be(0);
            (await _repository.FindDiagnosticsAsync(projectContext)).Should().BeEmpty();

            var replacement = CreateNode(
                id: $"{projectContext}::Diagnostic::replacement",
                name: "warning CS0002",
                type: CodeNodeType.Diagnostic,
                projectContext: projectContext,
                filePath: "src/Current.cs");
            await _repository.UpsertNodeAsync(replacement);
            (await _repository.CountDiagnosticsAsync(projectContext)).Should().Be(1);

            (await _repository.DeleteDiagnosticsAsync(projectContext)).Should().Be(1);
            await _repository.UpsertNodeAsync(replacement);
            (await _repository.FindDiagnosticsAsync(projectContext))
                .Should().ContainSingle(node => node.Id == replacement.Id);
            (await _repository.FindDiagnosticsAsync(otherProjectContext))
                .Should().ContainSingle(node => node.Id == otherProjectDiagnostic.Id);
            (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 10
            })).Should().Contain(node => node.Id == indexRun.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
            await _repository.DeleteProjectAsync(otherProjectContext);
        }
    }
}
