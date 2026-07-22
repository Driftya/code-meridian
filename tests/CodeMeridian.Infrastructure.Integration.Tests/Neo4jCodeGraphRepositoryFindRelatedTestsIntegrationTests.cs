using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindRelatedTestsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindRelatedTestsAsync_WithTemporaryTestFixture_ReturnsDirectTest()
    {
        var projectContext = $"Integration.RelatedTests.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var testNode = CreateNode(
            id: $"{projectContext}.Test",
            name: "TargetMethodTests",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"tests/{projectContext}/TargetTests.cs",
            namespaceName: $"{projectContext}.Tests");
        var caller = CreateNode(
            id: $"{projectContext}.Caller",
            name: "CallerMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Caller.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(testNode);
            await _repository.UpsertNodeAsync(caller);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = testNode.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = caller.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var related = await _repository.FindRelatedTestsAsync(target.Id, projectContext);

            related.Should().ContainSingle(match =>
                match.MatchType == "direct" && match.Node.Id == testNode.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindRelatedTestsAsync_WithStoredTestRole_ReturnsDirectTestWithoutNamingHeuristics()
    {
        var projectContext = $"Integration.RelatedTests.StoredRole.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var testNode = CreateNode(
            id: $"{projectContext}.Verifier",
            name: "Verifier",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Verifier.cs",
            namespaceName: $"{projectContext}.Verification")
        with
        {
            FileRole = IndexedFileRole.Test
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(testNode);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = testNode.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var related = await _repository.FindRelatedTestsAsync(target.Id, projectContext);

            related.Should().ContainSingle(match =>
                match.MatchType == "direct" && match.Node.Id == testNode.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
