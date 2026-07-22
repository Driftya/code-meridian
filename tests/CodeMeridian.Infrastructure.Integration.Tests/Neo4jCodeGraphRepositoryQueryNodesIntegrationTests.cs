using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryQueryNodesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task QueryNodesAsync_ForCodeMeridian_ReturnsKnownSurfaces()
    {
        var results = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 25
            });

        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task QueryNodesAsync_WithFilePathFilter_ReturnsNodesFromMatchingFile()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes");
        target!.FilePath.Should().NotBeNullOrWhiteSpace("exact symbol resolution depends on indexed file paths");

        var results = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = target.ProjectContext,
                FilePathFilter = target.FilePath,
                Limit = 25
            });

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(node =>
            string.Equals(node.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryNodesAsync_WithDuplicateMethodNamesAndFilePathFilter_ReturnsOnlyMatchingCandidate()
    {
        var projectContext = $"Integration.ResolveExact.{Guid.NewGuid():N}";
        var targetName = "BuildMinimalContextAsync";
        var applicationMethod = CreateNode(
            id: $"{projectContext}.Application.BuildMinimalContextAsync",
            name: targetName,
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CodebaseQueryService.Analytics.cs",
            namespaceName: $"{projectContext}.Application.Services",
            lineNumber: 938,
            sourceHash: "app-build-minimal-context");
        var mcpMethod = CreateNode(
            id: $"{projectContext}.Mcp.BuildMinimalContextAsync",
            name: targetName,
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CodebaseTools.Analytics.cs",
            namespaceName: $"{projectContext}.McpServer.Tools",
            lineNumber: 174,
            sourceHash: "mcp-build-minimal-context");

        try
        {
            await _repository!.UpsertNodeAsync(applicationMethod);
            await _repository.UpsertNodeAsync(mcpMethod);

            var results = await _repository.QueryNodesAsync(
                new CodeGraphQuery
                {
                    ProjectContext = projectContext,
                    NameFilter = targetName,
                    FilePathFilter = applicationMethod.FilePath,
                    Limit = 10
                });

            results.Should().ContainSingle(node => node.Id == applicationMethod.Id);
            results.Should().NotContain(node => node.Id == mcpMethod.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
