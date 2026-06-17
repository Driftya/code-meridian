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

        // GDS fails if the projection lists relationship types that do not exist in the current graph.
        // Project the dependency types that are guaranteed to exist after indexing.
        const string projectCypher = """
            CALL gds.graph.project($graphName, 'CodeNode', 'Calls')
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.pageRank.stream($graphName)
            YIELD nodeId, score
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContextNormalized = (object?)Normalize(projectContext), limit },
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
            CALL gds.graph.project($graphName, 'CodeNode', 'Calls')
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.betweenness.stream($graphName)
            YIELD nodeId, score
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContextNormalized = (object?)Normalize(projectContext), limit },
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
              Calls: {orientation: 'UNDIRECTED'}
            })
            YIELD graphName, nodeCount, relationshipCount
            """;

        const string streamCypher = """
            CALL gds.louvain.stream($graphName)
            YIELD nodeId, communityId
            MATCH (n:CodeNode) WHERE id(n) = nodeId
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            RETURN n, communityId
            ORDER BY communityId, n.name
            LIMIT 200
            """;

        return await RunGdsStreamAsync(session, graphName, projectCypher, streamCypher,
            new { graphName, projectContextNormalized = (object?)Normalize(projectContext) },
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
              AND ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
            RETURN node, score
            LIMIT $topK
            """;

        try
        {
            var cursor = await session.RunAsync(cypher, new
            {
                nodeId,
                projectContextNormalized = (object?)Normalize(projectContext),
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

    public async Task<IReadOnlyList<(CodeNode Node, double Score)>> FindHybridMatchesAsync(
        float[] queryEmbedding,
        string? nearNodeId = null,
        int maxHops = 3,
        string? projectContext = null,
        bool excludeTests = true,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var hops = Math.Clamp(maxHops, 1, 8);
        IReadOnlyCollection<string>? allowedNodeIds = null;
        if (!string.IsNullOrWhiteSpace(nearNodeId))
        {
            var neighborhoodCypher =
                "MATCH (anchor:CodeNode {id: $nearNodeId})-[:Calls|Uses|DependsOn*1.." + hops + "]-(node:CodeNode) " +
                "RETURN DISTINCT node.id AS id";

            var neighborhoodCursor = await session.RunAsync(neighborhoodCypher, new { nearNodeId });
            var neighborhoodIds = new List<string>();
            await foreach (var record in neighborhoodCursor.WithCancellation(cancellationToken))
                neighborhoodIds.Add(record["id"].As<string>());
            allowedNodeIds = neighborhoodIds;
        }

        var roleFilter = excludeTests ? "AND coalesce(node.fileRole, 'Unknown') <> 'Test'" : string.Empty;

        var cypher = $"""
            MATCH (node:CodeNode)
            WHERE node.embedding IS NOT NULL
              AND ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
              {roleFilter}
            WITH node, vector.similarity.cosine(node.embedding, $embedding) AS score
            RETURN node, score
            ORDER BY score DESC
            LIMIT $topKPlus
            """;

        try
        {
            var cursor = await session.RunAsync(cypher, new
            {
                embedding = queryEmbedding,
                nearNodeId,
                projectContextNormalized = (object?)Normalize(projectContext),
                topK,
                topKPlus = topK + 1
            });

            var results = new List<(CodeNode, double)>();
            await foreach (var record in cursor.WithCancellation(cancellationToken))
                results.Add((MapToCodeNode(record["node"].As<INode>()), record["score"].As<double>()));

            if (allowedNodeIds is not null)
            {
                var allowedLookup = allowedNodeIds.ToHashSet(StringComparer.Ordinal);
                results = results.Where(item => allowedLookup.Contains(item.Item1.Id)).ToList();
            }

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
    // Duplicate-code workflow query.
    /// <summary>
    /// Find semantically similar method/class pairs for duplicate-code review.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicateCandidatesAsync(
        string? projectContext = null,
        string? namespaceFilter = null,
        CodeNodeType? nodeType = null,
        int minLineCount = 5,
        double minSimilarity = 0.88,
        bool excludeTests = true,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var sourceRole = FileRoleExpression("source");
        var candidateRole = FileRoleExpression("candidate");
        var testRole = FileRoleExpression("test");
        var cypher = $@"
            MATCH (source:CodeNode)
            WHERE source.embedding IS NOT NULL
              AND source.type IN $nodeTypes
              AND coalesce(source.lineCount, 0) >= $minLineCount
              AND ($projectContextNormalized IS NULL OR source.projectContextNormalized = $projectContextNormalized)
              AND ($namespaceFilterNormalized IS NULL OR source.namespaceNormalized CONTAINS $namespaceFilterNormalized)
              AND (
                $excludeTests = false OR
                {sourceRole} <> 'Test'
              )
              AND {sourceRole} IN ['Source', 'Test', 'Unknown']
              AND {sourceRole} <> 'Migration'
              AND {sourceRole} <> 'Snapshot'
              AND {sourceRole} <> 'Generated'
              AND {sourceRole} <> 'BuildArtifact'
            WITH source
            ORDER BY coalesce(source.lineCount, 0) DESC
            LIMIT $candidatePool

            MATCH (candidate:CodeNode)
            WHERE candidate.embedding IS NOT NULL
              AND candidate.id > source.id
              AND candidate.type = source.type
              AND coalesce(candidate.lineCount, 0) >= $minLineCount
              AND ($projectContextNormalized IS NULL OR candidate.projectContextNormalized = $projectContextNormalized)
              AND ($namespaceFilterNormalized IS NULL OR candidate.namespaceNormalized CONTAINS $namespaceFilterNormalized)
              AND (
                $excludeTests = false OR
                {candidateRole} <> 'Test'
              )
              AND {candidateRole} IN ['Source', 'Test', 'Unknown']
              AND {candidateRole} <> 'Migration'
              AND {candidateRole} <> 'Snapshot'
              AND {candidateRole} <> 'Generated'
              AND {candidateRole} <> 'BuildArtifact'
            WITH source, candidate, vector.similarity.cosine(source.embedding, candidate.embedding) AS score
            WHERE score >= $minSimilarity
            CALL {{
              WITH source
              MATCH (source)<-[r:Calls|Uses]-(:CodeNode)
              RETURN count(r) AS sourceFanIn
            }}
            CALL {{
              WITH candidate
              MATCH (candidate)<-[r:Calls|Uses]-(:CodeNode)
              RETURN count(r) AS candidateFanIn
            }}
            CALL {{
              WITH source
              OPTIONAL MATCH (test:CodeNode)-[:Calls]->(source)
              WHERE {testRole} = 'Test'
              RETURN count(test) > 0 AS sourceHasTestCoverage
            }}
            CALL {{
              WITH candidate
              OPTIONAL MATCH (test:CodeNode)-[:Calls]->(candidate)
              WHERE {testRole} = 'Test'
              RETURN count(test) > 0 AS candidateHasTestCoverage
            }}
            RETURN source, candidate, score, sourceFanIn, candidateFanIn, sourceHasTestCoverage, candidateHasTestCoverage
            ORDER BY score DESC, (sourceFanIn + candidateFanIn) DESC
            LIMIT $limit
            ";

        var nodeTypes = nodeType.HasValue
            ? [nodeType.Value.ToString()]
            : new[] { CodeNodeType.Method.ToString(), CodeNodeType.Class.ToString() };

        try
        {
            var cursor = await session.RunAsync(cypher, new
            {
                projectContextNormalized = (object?)Normalize(projectContext),
                namespaceFilterNormalized = (object?)Normalize(namespaceFilter),
                nodeTypes,
                minLineCount,
                minSimilarity,
                excludeTests,
                limit,
                candidatePool = Math.Max(limit * 10, 100)
            });

            var results = new List<DuplicateCandidate>();
            await foreach (var record in cursor.WithCancellation(cancellationToken))
            {
                results.Add(new DuplicateCandidate(
                    MapToCodeNode(record["source"].As<INode>()),
                    MapToCodeNode(record["candidate"].As<INode>()),
                    record["score"].As<double>(),
                    record["sourceFanIn"].As<int>(),
                    record["candidateFanIn"].As<int>(),
                    record["sourceHasTestCoverage"].As<bool>(),
                    record["candidateHasTestCoverage"].As<bool>()));
            }

            return results;
        }
        catch (Exception ex) when (ex.Message.Contains("vector", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("embedding", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Vector similarity unavailable for duplicate-candidate discovery.");
            return [];
        }
    }

    /// <summary>
    /// Encapsulates the GDS project/stream/drop lifecycle.
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
