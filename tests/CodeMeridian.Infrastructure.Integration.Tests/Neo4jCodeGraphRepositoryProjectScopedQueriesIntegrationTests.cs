using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryProjectScopedQueriesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task ProjectScopedQueries_AreCaseInsensitive()
    {
        var projectContext = $"Integration.Case.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target");

        try
        {
            await _repository!.UpsertNodeAsync(node);

            var lowerCaseProject = projectContext.ToLowerInvariant();
            var matches = await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = lowerCaseProject,
                NameFilter = "Target",
                Limit = 10
            });
            var count = await _repository.CountCodeNodesAsync(lowerCaseProject);

            matches.Should().Contain(match => match.Id == node.Id);
            count.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
