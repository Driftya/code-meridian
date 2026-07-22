using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryUpsertNodeIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task UpsertNodeAsync_WithSameSourceHash_DoesNotAdvanceContentUpdateMetadata()
    {
        var projectContext = $"Integration.SourceHash.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            sourceHash: "abc123");

        try
        {
            await _repository!.UpsertNodeAsync(node);
            var first = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            await Task.Delay(5);
            await _repository.UpsertNodeAsync(node with { Summary = "Metadata refresh with identical source." });
            var second = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            second.SourceHash.Should().Be("abc123");
            second.UpdatedAt.Should().Be(first.UpdatedAt);
            second.LastIndexedAt.Should().BeAfter(first.LastIndexedAt!.Value);
            second.ChangeCount.Should().Be(first.ChangeCount);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task UpsertNodeAsync_WithDifferentSourceHash_AdvancesContentUpdateMetadata()
    {
        var projectContext = $"Integration.SourceHashChange.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            sourceHash: "abc123");

        try
        {
            await _repository!.UpsertNodeAsync(node);
            var first = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            await Task.Delay(5);
            await _repository.UpsertNodeAsync(node with { SourceHash = "def456" });
            var second = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            second.SourceHash.Should().Be("def456");
            second.UpdatedAt.Should().BeAfter(first.UpdatedAt!.Value);
            second.LastIndexedAt.Should().BeAfter(first.LastIndexedAt!.Value);
            second.ChangeCount.Should().Be(first.ChangeCount + 1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
