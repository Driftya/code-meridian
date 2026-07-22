using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindLargeNodesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindLargeNodesAsync_WithoutStoredFileRole_IncludesConfigurationNamedNodeAsUnknown()
    {
        var projectContext = $"Integration.LargeNodes.Unknown.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 450
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var results = await _repository.FindLargeNodesAsync(projectContext, classThreshold: 300, methodThreshold: 40);

            results.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
