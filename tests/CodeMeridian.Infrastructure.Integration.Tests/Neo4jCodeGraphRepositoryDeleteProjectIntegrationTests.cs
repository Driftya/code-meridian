using CodeMeridian.Core.CodeGraph;
using FluentAssertions;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryDeleteProjectIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task DeleteProjectAsync_DeletesMultipleBatchesAndCaseVariants()
    {
        const int nodeCount = 501;
        var projectContext = $"Integration.DeleteProject.{Guid.NewGuid():N}";
        var projectContextNormalized = projectContext.ToLowerInvariant();
        var otherProjectContext = $"{projectContext}.Other";
        var options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");

        await using var driver = GraphDatabase.Driver(
            options.Uri,
            AuthTokens.Basic(options.Username, options.Password));
        await using var session = driver.AsyncSession();

        try
        {
            var cursor = await session.RunAsync(
                """
                UNWIND range(1, $nodeCount) AS nodeIndex
                CREATE (:CodeNode {
                    id: $idPrefix + toString(nodeIndex),
                    name: 'Batch node ' + toString(nodeIndex),
                    type: 'Method',
                    projectContext: CASE
                        WHEN nodeIndex % 2 = 0 THEN $upperProjectContext
                        ELSE $lowerProjectContext
                    END,
                    projectContextNormalized: $projectContextNormalized
                })
                """,
                new
                {
                    nodeCount,
                    idPrefix = $"{projectContext}::Method::",
                    upperProjectContext = projectContext.ToUpperInvariant(),
                    lowerProjectContext = projectContext.ToLowerInvariant(),
                    projectContextNormalized
                });
            await cursor.ConsumeAsync();

            await _repository!.UpsertNodeAsync(CreateNode(
                id: $"{otherProjectContext}::Method::Keep",
                name: "Keep",
                type: CodeNodeType.Method,
                projectContext: otherProjectContext,
                filePath: $"src/{otherProjectContext}/Keep.cs"));

            (await _repository.CountCodeNodesAsync(projectContext)).Should().Be(nodeCount);

            await _repository.DeleteProjectAsync(projectContext.ToUpperInvariant());

            (await _repository.CountCodeNodesAsync(projectContext)).Should().Be(0);
            (await _repository.CountCodeNodesAsync(otherProjectContext)).Should().Be(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
            await _repository.DeleteProjectAsync(otherProjectContext);
        }
    }
}
