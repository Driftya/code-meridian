using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindDiagnosticsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindDiagnosticsAsync_WithTemporaryDiagnosticFixture_ReturnsMatchingDiagnostics()
    {
        var projectContext = $"Integration.Diagnostics.{Guid.NewGuid():N}";
        var filePath = $"src/{projectContext}/Target.cs";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetClass",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var diagnostic = CreateNode(
            id: $"{projectContext}.Diag",
            name: "Error CS9999",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: target.Namespace,
            lineNumber: 12,
            summary: "Synthetic diagnostic for integration coverage");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(diagnostic);

            var diagnostics = await _repository.FindDiagnosticsAsync(projectContext, severity: "error");

            diagnostics.Should().ContainSingle(node => node.Id == diagnostic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
