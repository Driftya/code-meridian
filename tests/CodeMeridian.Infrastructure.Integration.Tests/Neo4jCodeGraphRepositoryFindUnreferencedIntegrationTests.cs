using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindUnreferencedIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindUnreferencedAsync_WithTemporaryFixture_ReturnsIsolatedNode()
    {
        var projectContext = $"Integration.Unreferenced.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "UnreferencedClass",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var unreferenced = await _repository.FindUnreferencedAsync(projectContext);

            unreferenced.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
