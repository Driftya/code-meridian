using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindCoverageGapsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindCoverageGapsAsync_WithTemporaryProductionNode_ReturnsThatNode()
    {
        var projectContext = $"Integration.Coverage.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.CoverageTarget",
            name: "CoverageTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CoverageTarget.cs",
            namespaceName: $"{projectContext}.Coverage");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithStoredConfigurationRole_ExcludesThatNode()
    {
        var projectContext = $"Integration.Coverage.Config.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            FileRole = IndexedFileRole.Configuration
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().NotContain(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithoutStoredFileRole_TreatsNodeAsUnknown()
    {
        var projectContext = $"Integration.Coverage.Unknown.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
