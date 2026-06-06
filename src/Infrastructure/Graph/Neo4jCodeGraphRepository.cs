using System.Text;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

/// <summary>
/// Stores and queries the code knowledge graph in Neo4j.
/// Nodes represent code elements (classes, methods, etc.).
/// Edges represent structural relationships (calls, inherits, etc.).
/// Structural analytics are in Neo4jCodeGraphRepository.Analytics.cs.
/// GDS (Graph Data Science) queries are in Neo4jCodeGraphRepository.Gds.cs.
/// </summary>
public sealed partial class Neo4jCodeGraphRepository : ICodeGraphRepository, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jCodeGraphRepository> _logger;
    private readonly int _embeddingDimensions;

    public Neo4jCodeGraphRepository(
        IOptions<Neo4jOptions> options,
        ILogger<Neo4jCodeGraphRepository> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _embeddingDimensions = opts.EmbeddingDimensions;
        _driver = GraphDatabase.Driver(
            opts.Uri,
            AuthTokens.Basic(opts.Username, opts.Password),
            config => config
                .WithMaxConnectionPoolSize(opts.MaxConnectionPoolSize)
                .WithConnectionTimeout(TimeSpan.FromSeconds(opts.ConnectionTimeoutSeconds)));
    }

    /// <summary>Creates indexes and constraints on first run.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Neo4j code graph schema...");

        await using var session = _driver.AsyncSession();

        await session.ExecuteWriteAsync(async tx =>
        {
            await (await tx.RunAsync(
                "CREATE CONSTRAINT codenode_id IF NOT EXISTS FOR (n:CodeNode) REQUIRE n.id IS UNIQUE")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_name IF NOT EXISTS FOR (n:CodeNode) ON (n.name)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_name_normalized IF NOT EXISTS FOR (n:CodeNode) ON (n.nameNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE TEXT INDEX codenode_name_normalized_text IF NOT EXISTS FOR (n:CodeNode) ON (n.nameNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_project IF NOT EXISTS FOR (n:CodeNode) ON (n.projectContext)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_project_normalized IF NOT EXISTS FOR (n:CodeNode) ON (n.projectContextNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_namespace_normalized IF NOT EXISTS FOR (n:CodeNode) ON (n.namespaceNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE TEXT INDEX codenode_namespace_normalized_text IF NOT EXISTS FOR (n:CodeNode) ON (n.namespaceNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_filepath_normalized IF NOT EXISTS FOR (n:CodeNode) ON (n.filePathNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE TEXT INDEX codenode_filepath_normalized_text IF NOT EXISTS FOR (n:CodeNode) ON (n.filePathNormalized)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE FULLTEXT INDEX codenode_fulltext IF NOT EXISTS FOR (n:CodeNode) ON EACH [n.name, n.summary]")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_createdat IF NOT EXISTS FOR (n:CodeNode) ON (n.createdAt)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_updatedat IF NOT EXISTS FOR (n:CodeNode) ON (n.updatedAt)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_linecount IF NOT EXISTS FOR (n:CodeNode) ON (n.lineCount)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX codenode_changecount IF NOT EXISTS FOR (n:CodeNode) ON (n.changeCount)")).ConsumeAsync();
        });

        await session.ExecuteWriteAsync(async tx =>
        {
            await (await tx.RunAsync(
                """
                MATCH (n:CodeNode)
                WHERE n.nameNormalized IS NULL
                   OR (n.namespace IS NOT NULL AND n.namespaceNormalized IS NULL)
                   OR (n.filePath IS NOT NULL AND n.filePathNormalized IS NULL)
                   OR (n.projectContext IS NOT NULL AND n.projectContextNormalized IS NULL)
                SET n.nameNormalized = toLower(n.name),
                    n.namespaceNormalized = CASE WHEN n.namespace IS NULL THEN NULL ELSE toLower(n.namespace) END,
                    n.filePathNormalized = CASE WHEN n.filePath IS NULL THEN NULL ELSE toLower(n.filePath) END,
                    n.projectContextNormalized = CASE WHEN n.projectContext IS NULL THEN NULL ELSE toLower(n.projectContext) END
                """
            )).ConsumeAsync();
        });

        // Vector index must be created outside an explicit transaction in Neo4j 5.x
        await using var vectorSession = _driver.AsyncSession();
        try
        {
            await (await vectorSession.RunAsync(
                "CREATE VECTOR INDEX codenode_embeddings IF NOT EXISTS " +
                "FOR (n:CodeNode) ON (n.embedding) " +
                $"OPTIONS {{indexConfig: {{`vector.dimensions`: {_embeddingDimensions}, `vector.similarity_function`: 'cosine'}}}}")).ConsumeAsync();
        }
        catch (Exception ex)
        {
            // Vector index may already exist or Neo4j edition may not support it — non-fatal
            _logger.LogWarning(ex, "Could not create vector index — native embedding search will be unavailable.");
        }

        _logger.LogInformation("Neo4j code graph schema ready.");
    }

    public async Task<IReadOnlyList<CodeNode>> QueryNodesAsync(
        CodeGraphQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Prefer full-text search when a semantic query is provided
        if (!string.IsNullOrWhiteSpace(query.SemanticQuery))
        {
            return await FullTextSearchAsync(session, query, cancellationToken);
        }

        return await FilteredQueryAsync(session, query, cancellationToken);
    }

    public async Task<IReadOnlyList<CodeEdge>> QueryEdgesAsync(
        string nodeId,
        int depth = 1,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cypher = $$"""
            MATCH (source:CodeNode {id: $nodeId})-[r*1..{{depth}}]-(target:CodeNode)
            UNWIND r AS rel
            RETURN DISTINCT rel
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var edges = new List<CodeEdge>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var rel = record["rel"].As<IRelationship>();
            edges.Add(MapToCodeEdge(rel));
        }

        return edges;
    }

    public async Task<long> CountCodeNodesAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            RETURN count(n) AS count
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var record = await cursor.SingleAsync();
        return record["count"].As<long>();
    }

    public async Task<long> CountCallEdgesAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (s:CodeNode)-[r:Calls]->(t:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR s.projectContextNormalized = $projectContextNormalized OR t.projectContextNormalized = $projectContextNormalized)
            RETURN count(r) AS count
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var record = await cursor.SingleAsync();
        return record["count"].As<long>();
    }

    public async Task<long> CountDiagnosticsAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.type = 'Diagnostic'
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            RETURN count(n) AS count
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var record = await cursor.SingleAsync();
        return record["count"].As<long>();
    }

    public async Task<string> GetSubgraphSummaryAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode {id: $nodeId})
            OPTIONAL MATCH (n)-[r]->(related:CodeNode)
            RETURN n, collect({relType: type(r), name: related.name, type: related.type}) AS relations
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var records = await cursor.ToListAsync();

        if (records.Count == 0) return string.Empty;

        var record = records[0];
        var node = MapToCodeNode(record["n"].As<INode>());
        var relations = record["relations"].As<List<IDictionary<string, object>>>();

        var sb = new StringBuilder();
        sb.AppendLine($"**{node.Type}: {node.Name}**");

        if (!string.IsNullOrEmpty(node.Summary))
            sb.AppendLine(node.Summary);

        if (!string.IsNullOrEmpty(node.FilePath))
            sb.AppendLine($"File: `{node.FilePath}`");

        if (relations.Count > 0)
        {
            sb.AppendLine("Relations:");
            foreach (var rel in relations.Take(10).Where(r => r["name"] is not null))
                sb.AppendLine($"  - {rel["relType"]} → {rel["type"]}:{rel["name"]}");
        }

        return sb.ToString();
    }

    public async Task UpsertNodeAsync(CodeNode node, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MERGE (n:CodeNode {id: $id})
            ON CREATE SET n.createdAt = $now, n.changeCount = 0
            SET n.name           = $name,
                n.nameNormalized = $nameNormalized,
                n.type           = $type,
                n.namespace      = $namespace,
                n.namespaceNormalized = $namespaceNormalized,
                n.filePath       = $filePath,
                n.filePathNormalized = $filePathNormalized,
                n.lineNumber     = $lineNumber,
                n.lineCount      = $lineCount,
                n.summary        = $summary,
                n.sourceSnippet  = $sourceSnippet,
                n.projectContext = $projectContext,
                n.projectContextNormalized = $projectContextNormalized,
                n.changeCount    = coalesce(n.changeCount, 0) + 1,
                n.updatedAt      = $now
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new
            {
                id = node.Id,
                name = node.Name,
                nameNormalized = Normalize(node.Name),
                type = node.Type.ToString(),
                @namespace = node.Namespace,
                namespaceNormalized = Normalize(node.Namespace),
                filePath = node.FilePath,
                filePathNormalized = Normalize(node.FilePath),
                lineNumber = (object?)node.LineNumber,
                lineCount = (object?)node.LineCount,
                summary = node.Summary,
                sourceSnippet = node.SourceSnippet,
                projectContext = node.ProjectContext,
                projectContextNormalized = Normalize(node.ProjectContext),
                now
            });
            await cursor.ConsumeAsync();
        });

        // Store embedding separately to avoid overwriting existing embeddings with null
        if (node.Embedding is { Length: > 0 } embedding)
        {
            const string embCypher = "MATCH (n:CodeNode {id: $id}) SET n.embedding = $embedding";
            await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(embCypher, new { id = node.Id, embedding });
                await cursor.ConsumeAsync();
            });
        }
    }

    public async Task UpsertEdgeAsync(CodeEdge edge, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Edge type is an enum value — safe to interpolate (no user input)
        var cypher = $$"""
            MATCH (s:CodeNode {id: $sourceId})
            MATCH (t:CodeNode {id: $targetId})
            MERGE (s)-[r:{{edge.Type}}]->(t)
            SET r.isAsync     = $isAsync,
                r.callSite    = $callSite,
                r.paramCount  = $paramCount,
                r.confidence  = $confidence
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new
            {
                sourceId   = edge.SourceId,
                targetId   = edge.TargetId,
                isAsync    = (object?)edge.IsAsync,
                callSite   = (object?)edge.CallSite,
                paramCount = (object?)edge.ParamCount,
                confidence = (object?)edge.Confidence
            });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteProjectAsync(string projectContext, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.projectContextNormalized = $projectContextNormalized
            DETACH DELETE n
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new { projectContextNormalized = Normalize(projectContext) });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteFileAsync(
        string projectContext,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.projectContextNormalized = $projectContextNormalized
              AND n.filePathNormalized = $filePathNormalized
            DETACH DELETE n
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new
            {
                projectContextNormalized = Normalize(projectContext),
                filePathNormalized = Normalize(filePath)
            });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteDiagnosticsAsync(string projectContext, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.projectContextNormalized = $projectContextNormalized
              AND n.type = 'Diagnostic'
            DETACH DELETE n
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new { projectContextNormalized = Normalize(projectContext) });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            DETACH DELETE n
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher);
            await cursor.ConsumeAsync();
        });
    }

    public async Task<IReadOnlyList<CodeNode>> FindDiagnosticsAsync(
        string? projectContext = null,
        string? severity = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.type = 'Diagnostic'
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND ($severityNormalized IS NULL OR n.nameNormalized STARTS WITH $severityNormalized)
            RETURN n
            ORDER BY n.filePath, n.lineNumber, n.name
            LIMIT 200
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
            severityNormalized = (object?)Normalize(severity)
        });

        var nodes = new List<CodeNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            nodes.Add(MapToCodeNode(record["n"].As<INode>()));

        return nodes;
    }

    public async Task<IReadOnlyList<CodeNode>> FindDiagnosticsForNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (target:CodeNode {id: $nodeId})
            MATCH (diag:CodeNode)
            WHERE diag.type = 'Diagnostic'
              AND diag.projectContextNormalized = target.projectContextNormalized
              AND diag.filePathNormalized = target.filePathNormalized
            RETURN diag AS n
            ORDER BY abs(coalesce(diag.lineNumber, 0) - coalesce(target.lineNumber, 0)), diag.lineNumber
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var nodes = new List<CodeNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            nodes.Add(MapToCodeNode(record["n"].As<INode>()));

        return nodes;
    }

    public async Task<DateTimeOffset?> GetMostRecentCodeUpdateAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND n.updatedAt IS NOT NULL
            RETURN max(n.updatedAt) AS updatedAt
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var records = await cursor.ToListAsync();

        if (records.Count == 0)
            return null;

        var raw = records[0]["updatedAt"];
        if (raw is null || raw == DBNull.Value)
            return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(raw.As<long>());
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<CodeNode>> FullTextSearchAsync(
        IAsyncSession session,
        CodeGraphQuery query,
        CancellationToken cancellationToken)
    {
        // Full-text query; project filter applied post-retrieval if needed
        const string cypher = """
            CALL db.index.fulltext.queryNodes('codenode_fulltext', $query)
            YIELD node, score
            WHERE ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
            RETURN node
            ORDER BY score DESC
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            query = query.SemanticQuery,
            projectContextNormalized = (object?)Normalize(query.ProjectContext),
            limit = query.Limit
        });

        var nodes = new List<CodeNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            nodes.Add(MapToCodeNode(record["node"].As<INode>()));

        return nodes;
    }

    private static async Task<IReadOnlyList<CodeNode>> FilteredQueryAsync(
        IAsyncSession session,
        CodeGraphQuery query,
        CancellationToken cancellationToken)
    {
        var conditions = new List<string>();
        if (query.ProjectContext is not null) conditions.Add("n.projectContextNormalized = $projectContextNormalized");
        if (query.NameFilter is not null) conditions.Add("n.nameNormalized CONTAINS $nameFilterNormalized");
        if (query.FilePathFilter is not null) conditions.Add("n.filePathNormalized CONTAINS $filePathFilterNormalized");
        if (query.TypeFilter.HasValue) conditions.Add("n.type = $typeFilter");

        var where = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : string.Empty;
        var cypher = $"MATCH (n:CodeNode) {where} RETURN n LIMIT $limit";

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(query.ProjectContext),
            nameFilterNormalized = (object?)Normalize(query.NameFilter),
            filePathFilterNormalized = (object?)Normalize(query.FilePathFilter),
            typeFilter = (object?)query.TypeFilter?.ToString(),
            limit = query.Limit
        });

        var nodes = new List<CodeNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            nodes.Add(MapToCodeNode(record["n"].As<INode>()));

        return nodes;
    }

    private static CodeNode MapToCodeNode(INode node)
    {
        var props = node.Properties;

        DateTimeOffset? ReadTimestamp(string key)
        {
            if (!props.TryGetValue(key, out var raw) || raw is null) return null;
            var ms = raw.As<long?>();
            return ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
        }

        return new CodeNode
        {
            Id = props["id"].As<string>(),
            Name = props["name"].As<string>(),
            Type = Enum.Parse<CodeNodeType>(props["type"].As<string>(), ignoreCase: true),
            Namespace = props.TryGetValue("namespace", out var ns) ? ns?.As<string>() : null,
            FilePath = props.TryGetValue("filePath", out var fp) ? fp?.As<string>() : null,
            LineNumber = props.TryGetValue("lineNumber", out var ln) ? ln?.As<int?>() : null,
            LineCount = props.TryGetValue("lineCount", out var lc) ? lc?.As<int?>() : null,
            Summary = props.TryGetValue("summary", out var sum) ? sum?.As<string>() : null,
            SourceSnippet = props.TryGetValue("sourceSnippet", out var ss) ? ss?.As<string>() : null,
            ProjectContext = props.TryGetValue("projectContext", out var pc) ? pc?.As<string>() : null,
            ChangeCount = props.TryGetValue("changeCount", out var cc) ? cc?.As<int?>() : null,
            CreatedAt = ReadTimestamp("createdAt"),
            UpdatedAt = ReadTimestamp("updatedAt")
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();

    private static CodeEdge MapToCodeEdge(IRelationship rel)
    {
        var props = rel.Properties;
        return new CodeEdge
        {
            SourceId   = rel.StartNodeElementId,
            TargetId   = rel.EndNodeElementId,
            Type       = Enum.Parse<CodeEdgeType>(rel.Type, ignoreCase: true),
            IsAsync    = props.TryGetValue("isAsync",    out var ia)  ? ia?.As<bool?>()   : null,
            CallSite   = props.TryGetValue("callSite",   out var cs)  ? cs?.As<string>()  : null,
            ParamCount = props.TryGetValue("paramCount", out var pc)  ? pc?.As<int?>()    : null,
            Confidence = props.TryGetValue("confidence", out var con) ? con?.As<double?>(): null
        };
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
