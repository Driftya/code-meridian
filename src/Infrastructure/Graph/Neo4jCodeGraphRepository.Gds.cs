using CodeMeridian.Core.CodeGraph;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

/// <summary>
/// Graph Data Science (GDS) plugin queries — PageRank, Betweenness, Louvain, vector similarity.
/// All GDS methods share a common project/stream/drop lifecycle via RunGdsStreamAsync.
/// Analytics queries live in Neo4jCodeGraphRepository.Analytics.cs.
/// </summary>
public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<IReadOnlyList<(CodeNode Node, double Score)>> GetPageRankAsync(
        string? projectContext = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var graphName = $"cm_pr_{Guid.NewGuid():N}";

        const string projectCypher = """
            CALL gds.graph.project($graphName, 'CodeNode', ['Calls', 'Uses', 'DependsOn'])
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.pageRank.stream($graphName)
            YIELD nodeId, score
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContext = (object?)projectContext, limit },
            r => (MapToCodeNode(r["n"].As<INode>()), r["score"].As<double>()),
            cancellationToken);
    }

    public async Task<IReadOnlyList<(CodeNode Node, double Score)>> GetBetweennessAsync(
        string? projectContext = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var graphName = $"cm_bc_{Guid.NewGuid():N}";

        const string projectCypher = """
            CALL gds.graph.project($graphName, 'CodeNode', ['Calls', 'Uses', 'DependsOn'])
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.betweenness.stream($graphName)
            YIELD nodeId, score
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContext = (object?)projectContext, limit },
            r => (MapToCodeNode(r["n"].As<INode>()), r["score"].As<double>()),
            cancellationToken);
    }

    public async Task<IReadOnlyList<(CodeNode Node, long Community)>> FindNaturalModulesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var graphName = $"cm_lv_{Guid.NewGuid():N}";

        // Louvain requires undirected relationships
        const string projectCypher = """
            CALL gds.graph.project($graphName, 'CodeNode', {
              Calls:     {orientation: 'UNDIRECTED'},
              Uses:      {orientation: 'UNDIRECTED'},
              DependsOn: {orientation: 'UNDIRECTED'}
            })
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.louvain.stream($graphName)
            YIELD nodeId, communityId
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
            RETURN n, communityId
            ORDER BY communityId, n.name
            LIMIT 200
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContext = (object?)projectContext },
            r => (MapToCodeNode(r["n"].As<INode>()), r["communityId"].As<long>()),
            cancellationToken);
    }

    public async Task<IReadOnlyList<(CodeNode Node, double Score)>> FindSimilarToNodeAsync(
        string nodeId,
        string? projectContext = null,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Fetch the source node's embedding, then query the vector index
        const string cypher = """
            MATCH (source:CodeNode {id: $nodeId})
            WHERE source.embedding IS NOT NULL
            CALL db.index.vector.queryNodes('codenode_embeddings', $topKPlus, source.embedding)
            YIELD node, score
            WHERE node.id <> $nodeId
              AND ($projectContext IS NULL OR node.projectContext = $projectContext)
            RETURN node, score
            LIMIT $topK
            """;

        try
        {
            var cursor = await session.RunAsync(cypher, new
            {
                nodeId,
                projectContext = (object?)projectContext,
                topK,
                topKPlus = topK + 1
            });

            var results = new List<(CodeNode, double)>();
            await foreach (var record in cursor.WithCancellation(cancellationToken))
                results.Add((MapToCodeNode(record["node"].As<INode>()), record["score"].As<double>()));

            return results;
        }
        catch (Exception ex) when (ex.Message.Contains("vector", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("index", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Vector index not available. Ingest embeddings via ingest_code_node first.");
            return [];
        }
    }

    // ── Private helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Encapsulates the GDS project → stream → drop lifecycle.
    /// Projects a named in-memory graph, streams algorithm results, then drops the projection.
    /// The <paramref name="parameters"/> object must contain a <c>graphName</c> property.
    /// </summary>
    private static async Task<IReadOnlyList<T>> RunGdsStreamAsync<T>(
        IAsyncSession session,
        string graphName,
        string projectCypher,
        string streamCypher,
        object parameters,
        Func<IRecord, T> mapRecord,
        CancellationToken ct)
    {
        try
        {
            await (await session.RunAsync(projectCypher, new { graphName })).ConsumeAsync();

            var cursor = await session.RunAsync(streamCypher, parameters);
            var results = new List<T>();

            await foreach (var record in cursor.WithCancellation(ct))
                results.Add(mapRecord(record));

            return results;
        }
        finally
        {
            await (await session.RunAsync(
                "CALL gds.graph.drop($graphName, false) YIELD graphName",
                new { graphName })).ConsumeAsync();
        }
    }
}
