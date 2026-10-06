using FluentAssertions;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryInitializationIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task InitializeAsync_BackfillsMultipleBatchesAndPreservesExistingValuesOnRerun()
    {
        const int nodeCount = 1002;
        var projectContext = $"Integration.Initialization.{Guid.NewGuid():N}";
        var projectContextNormalized = projectContext.ToLowerInvariant();
        await using var driver = GraphDatabase.Driver(
            _options.Uri, AuthTokens.Basic(_options.Username, _options.Password));
        await using var session = driver.AsyncSession();

        try
        {
            await (await session.RunAsync(
                """
                UNWIND range(0, $nodeCount - 1) AS i
                CREATE (n:CodeNode {
                    id: $project + '::' + toString(i), name: 'LegacyMethod', type: 'Method',
                    namespace: 'Legacy.Namespace', filePath: 'src/Legacy.cs', projectContext: $project
                })
                CREATE (n)-[r:Calls]->(n)
                FOREACH (_ IN CASE WHEN i = 0 THEN [1] ELSE [] END |
                    SET n.nameNormalized = 'preserved-name', n.namespaceNormalized = 'legacy.namespace',
                        n.filePathNormalized = 'src/legacy.cs', n.projectContextNormalized = $normalized,
                        r.packageReferenceKey = 'existing-package-key')
                """, new { nodeCount, project = projectContext, normalized = projectContextNormalized })).ConsumeAsync();

            // The first run must complete several batches; the second must leave the result unchanged.
            for (var run = 0; run < 2; run++)
            {
                await _repository!.InitializeAsync();
                var record = await (await session.RunAsync(
                    """
                    MATCH (n:CodeNode {projectContext: $project})-[r:Calls]->(n)
                    RETURN count(n) AS nodes,
                        count(CASE WHEN n.nameNormalized = 'legacymethod' THEN 1 END) AS names,
                        count(CASE WHEN n.nameNormalized = 'preserved-name' THEN 1 END) AS preservedNames,
                        count(CASE WHEN n.namespaceNormalized = 'legacy.namespace'
                            AND n.filePathNormalized = 'src/legacy.cs'
                            AND n.projectContextNormalized = $normalized THEN 1 END) AS normalized,
                        count(CASE WHEN r.packageReferenceKey = '' THEN 1 END) AS backfilledKeys,
                        count(CASE WHEN r.packageReferenceKey = 'existing-package-key' THEN 1 END) AS preservedKeys
                    """, new { project = projectContext, normalized = projectContextNormalized })).SingleAsync();

                Convert.ToInt64(record["nodes"]).Should().Be(nodeCount);
                Convert.ToInt64(record["names"]).Should().Be(nodeCount - 1);
                Convert.ToInt64(record["preservedNames"]).Should().Be(1);
                Convert.ToInt64(record["normalized"]).Should().Be(nodeCount);
                Convert.ToInt64(record["backfilledKeys"]).Should().Be(nodeCount - 1);
                Convert.ToInt64(record["preservedKeys"]).Should().Be(1);
            }
        }
        finally
        {
            // Match the original project property so cleanup also works after a failed backfill.
            await (await session.RunAsync(
                "MATCH (n:CodeNode {projectContext: $project}) DETACH DELETE n",
                new { project = projectContext })).ConsumeAsync();
        }
    }
}
