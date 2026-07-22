using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryCountMethodsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task CountMethods_ForExistingGraph_ReturnReasonableValues()
    {
        var projectContext = $"Integration.Counts.{Guid.NewGuid():N}";
        var filePath = $"src/{projectContext}/Target.cs";
        var caller = CreateNode(
            id: $"{projectContext}.Caller",
            name: "Caller",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var callee = CreateNode(
            id: $"{projectContext}.Callee",
            name: "Callee",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var diagnostic = CreateNode(
            id: $"{projectContext}.Diag",
            name: "Error CS9999",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            summary: "Synthetic diagnostic for count coverage");

        try
        {
            await _repository!.UpsertNodeAsync(caller);
            await _repository.UpsertNodeAsync(callee);
            await _repository.UpsertNodeAsync(diagnostic);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = caller.Id,
                TargetId = callee.Id,
                Type = CodeEdgeType.Calls
            });

            var nodeCount = await _repository.CountCodeNodesAsync(projectContext);
            var callCount = await _repository.CountCallEdgesAsync(projectContext);
            var diagnosticCount = await _repository.CountDiagnosticsAsync(projectContext);

            nodeCount.Should().BeGreaterOrEqualTo(2);
            callCount.Should().BeGreaterOrEqualTo(1);
            diagnosticCount.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
