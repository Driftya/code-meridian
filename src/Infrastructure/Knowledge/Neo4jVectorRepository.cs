using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Knowledge;

/// <summary>
/// Stores and queries knowledge documents using Neo4j's built-in vector index.
/// Requires Neo4j 5.11+ with the vector index feature enabled.
/// </summary>
public sealed class Neo4jVectorRepository : IVectorRepository, IAsyncDisposable
{
    private const string IndexName = "knowledge_vector_index";
    private readonly IDriver _driver;
    private readonly int _dimensions;
    private readonly ILogger<Neo4jVectorRepository> _logger;

    public Neo4jVectorRepository(
        IOptions<Neo4jOptions> options,
        ILogger<Neo4jVectorRepository> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _dimensions = opts.EmbeddingDimensions;
        _driver = GraphDatabase.Driver(
            opts.Uri,
            AuthTokens.Basic(opts.Username, opts.Password));
    }

    /// <summary>Creates the vector and full-text indexes if they do not already exist.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Neo4j knowledge indexes (dims={Dims})...", _dimensions);

        await using var session = _driver.AsyncSession();

        await session.ExecuteWriteAsync(async tx =>
        {
            // Full-text index — always available, no embeddings required
            await (await tx.RunAsync(
                "CREATE FULLTEXT INDEX knowledge_fulltext IF NOT EXISTS " +
                "FOR (d:KnowledgeDocument) ON EACH [d.content, d.source]")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX knowledge_project_normalized IF NOT EXISTS FOR (d:KnowledgeDocument) ON (d.projectContextNormalized)")).ConsumeAsync();

            // Vector index — used when embeddings are available
            await (await tx.RunAsync($$"""
                CREATE VECTOR INDEX {{IndexName}} IF NOT EXISTS
                FOR (d:KnowledgeDocument) ON (d.embedding)
                OPTIONS {
                  indexConfig: {
                    `vector.dimensions`: {{_dimensions}},
                    `vector.similarity_function`: 'cosine'
                  }
                }
                """)).ConsumeAsync();
        });

        await session.ExecuteWriteAsync(async tx =>
        {
            await (await tx.RunAsync(
                """
                MATCH (d:KnowledgeDocument)
                WHERE d.projectContext IS NOT NULL AND d.projectContextNormalized IS NULL
                SET d.projectContextNormalized = toLower(d.projectContext)
                """
            )).ConsumeAsync();
        });

        _logger.LogInformation("Neo4j knowledge indexes ready.");
    }

    public async Task UpsertAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MERGE (d:KnowledgeDocument {id: $id})
            ON CREATE SET d.createdAt = $now
            SET d.content        = $content,
                d.source         = $source,
                d.projectContext = $projectContext,
                d.projectContextNormalized = $projectContextNormalized,
                d.embedding      = $embedding,
                d.relatedNodeIds = $relatedNodeIds,
                d.relatedDocumentIds = $relatedDocumentIds,
                d.metadataKind   = $metadataKind,
                d.updatedAt      = $now
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var mentionIds = ExtractMentionIds(document.Metadata);
        var relatedDocumentIds = ExtractRelatedDocumentIds(document.Metadata);
        var relatedNodeIds = mentionIds.Count > 0 ? string.Join(",", mentionIds) : null;
        var relatedDocsCsv = relatedDocumentIds.Count > 0 ? string.Join(",", relatedDocumentIds) : null;
        var metadataKind = document.Metadata.TryGetValue("kind", out var kind) ? kind : null;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new
            {
                id = document.Id,
                content = document.Content,
                source = document.Source,
                projectContext = document.ProjectContext,
                projectContextNormalized = Normalize(document.ProjectContext),
                embedding = document.Embedding,
                relatedNodeIds,
                relatedDocumentIds = relatedDocsCsv,
                metadataKind,
                now
            });
            await cursor.ConsumeAsync();

            await (await tx.RunAsync(
                """
                MATCH (d:KnowledgeDocument {id: $id})-[r:Mentions]->()
                DELETE r
                """,
                new { id = document.Id })).ConsumeAsync();

            if (mentionIds.Count > 0)
            {
                await (await tx.RunAsync(
                    """
                    UNWIND $mentionIds AS targetId
                    MATCH (d:KnowledgeDocument {id: $id})
                    MATCH (n:CodeNode {id: targetId})
                    MERGE (d)-[:Mentions]->(n)
                    """,
                    new
                    {
                        id = document.Id,
                        mentionIds
                    })).ConsumeAsync();
            }

            await (await tx.RunAsync(
                """
                MATCH (d:KnowledgeDocument {id: $id})-[r:References]->()
                DELETE r
                """,
                new { id = document.Id })).ConsumeAsync();

            if (relatedDocumentIds.Count > 0)
            {
                await (await tx.RunAsync(
                    """
                    UNWIND $targetIds AS targetId
                    MATCH (d:KnowledgeDocument {id: $id})
                    MATCH (t:KnowledgeDocument)
                    WHERE t.id = targetId OR t.source = targetId
                    MERGE (d)-[:References]->(t)
                    """,
                    new
                    {
                        id = document.Id,
                        targetIds = relatedDocumentIds
                    })).ConsumeAsync();
            }
        });
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(
        string? projectContext = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (d:KnowledgeDocument)
            WHERE ($projectContextNormalized IS NULL OR d.projectContextNormalized = $projectContextNormalized)
            RETURN d
            ORDER BY d.updatedAt DESC, d.id
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
            limit
        });

        var results = new List<KnowledgeDocument>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToDocument(record["d"].As<INode>()));

        return results;
    }

    public async Task<long> CountAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (d:KnowledgeDocument)
            WHERE ($projectContextNormalized IS NULL OR d.projectContextNormalized = $projectContextNormalized)
            RETURN count(d) AS count
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var record = await cursor.SingleAsync();
        return record["count"].As<long>();
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(
        float[] queryEmbedding,
        string? projectContext = null,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            CALL db.index.vector.queryNodes($indexName, $topK, $embedding)
            YIELD node, score
            WHERE ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
            RETURN node, score
            ORDER BY score DESC
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            indexName = IndexName,
            topK,
            embedding = queryEmbedding,
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var results = new List<KnowledgeDocument>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var node = record["node"].As<INode>();
            results.Add(MapToDocument(node));
        }

        return results;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> SearchByTextAsync(
        string query,
        string? projectContext = null,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var luceneQuery = EscapeLuceneQuery(query);

        const string cypher = """
            CALL db.index.fulltext.queryNodes('knowledge_fulltext', $query)
            YIELD node, score
            WHERE ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
            RETURN node, score
            ORDER BY score DESC
            LIMIT $topK
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            query = luceneQuery,
            projectContextNormalized = (object?)Normalize(projectContext),
            topK
        });

        var results = new List<KnowledgeDocument>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToDocument(record["node"].As<INode>()));

        return results;
    }

    private static string EscapeLuceneQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var sb = new System.Text.StringBuilder(query.Length * 2);
        foreach (var ch in query.Trim())
        {
            if ("+-!(){}[]^\"~*?:\\/".Contains(ch))
                sb.Append('\\');

            sb.Append(ch);
        }

        return sb.ToString();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = "MATCH (d:KnowledgeDocument {id: $id}) DELETE d";
        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new { id });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteSourceAsync(
        string projectContext,
        string source,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (d:KnowledgeDocument)
            WHERE d.projectContextNormalized = $projectContextNormalized
              AND d.source = $source
            DETACH DELETE d
            """;

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new { projectContextNormalized = Normalize(projectContext), source });
            await cursor.ConsumeAsync();
        });
    }

    public async Task DeleteProjectAsync(string projectContext, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (d:KnowledgeDocument)
            WHERE d.projectContextNormalized = $projectContextNormalized
            DELETE d
            """;
        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, new { projectContextNormalized = Normalize(projectContext) });
            await cursor.ConsumeAsync();
        });
    }

    private static KnowledgeDocument MapToDocument(INode node)
    {
        var props = node.Properties;

        DateTimeOffset? ReadTimestamp(string key)
        {
            if (!props.TryGetValue(key, out var raw) || raw is null) return null;
            var ms = raw.As<long?>();
            return ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
        }

        return new KnowledgeDocument
        {
            Id = props["id"].As<string>(),
            Content = props["content"].As<string>(),
            Source = props.TryGetValue("source", out var src) ? src?.As<string>() : null,
            ProjectContext = props.TryGetValue("projectContext", out var pc) ? pc?.As<string>() : null,
            CreatedAt = ReadTimestamp("createdAt"),
            UpdatedAt = ReadTimestamp("updatedAt"),
            Metadata = ReadMetadata(props)
        };
    }

    private static Dictionary<string, string> ReadMetadata(IReadOnlyDictionary<string, object?> props)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (props.TryGetValue("relatedNodeIds", out var relatedNodeIds) && relatedNodeIds is not null)
            metadata["relatedNodeIds"] = relatedNodeIds.As<string>();

        if (props.TryGetValue("metadataKind", out var metadataKind) && metadataKind is not null)
            metadata["kind"] = metadataKind.As<string>();

        return metadata;
    }

    private static List<string> ExtractMentionIds(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "relatedNodeIds", "relatedNodes", "mentions" })
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            return raw
                .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static List<string> ExtractRelatedDocumentIds(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "relatedDocumentIds", "references", "relatedDocuments" })
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            return raw
                .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
