using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindHotspotsIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindHotspotsAsync_WithTemporaryFixture_ReturnsHighFanInNode()
    {
        var projectContext = $"Integration.Hotspots.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "HotspotTarget",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs",
            namespaceName: $"{projectContext}.Production");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var hotspots = await _repository.FindHotspotsAsync(projectContext, limit: 10);

            hotspots.Should().ContainSingle(item =>
                item.Node.Id == target.Id && item.FanIn >= 2);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
