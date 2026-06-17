using CodeMeridian.Core.KeywordGraph;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed class Neo4jKeywordGraphRepository : IKeywordGraphRepository, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jKeywordGraphRepository> _logger;

    public Neo4jKeywordGraphRepository(
        IOptions<Neo4jOptions> options,
        ILogger<Neo4jKeywordGraphRepository> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _driver = GraphDatabase.Driver(
            opts.Uri,
            AuthTokens.Basic(opts.Username, opts.Password),
            config => config
                .WithMaxConnectionPoolSize(opts.MaxConnectionPoolSize)
                .WithConnectionTimeout(TimeSpan.FromSeconds(opts.ConnectionTimeoutSeconds)));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Neo4j keyword graph schema...");

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await (await tx.RunAsync(
                "CREATE CONSTRAINT keyword_identity IF NOT EXISTS FOR (k:Keyword) REQUIRE (k.projectContextNormalized, k.normalizedValue) IS UNIQUE")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX keyword_id IF NOT EXISTS FOR (k:Keyword) ON (k.id)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX keyword_value IF NOT EXISTS FOR (k:Keyword) ON (k.normalizedValue)")).ConsumeAsync();
            await (await tx.RunAsync(
                "CREATE INDEX keyword_frequency IF NOT EXISTS FOR (k:Keyword) ON (k.documentFrequency)")).ConsumeAsync();
        });

        _logger.LogInformation("Neo4j keyword graph schema ready.");
    }

    public async Task<IReadOnlyList<KeywordSourceNode>> GetKeywordSourceNodesAsync(
        KeywordSourceNodeQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            CALL {
              MATCH (node:CodeNode)
              WHERE ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
              RETURN
                node.id AS id,
                node.projectContext AS projectContext,
                coalesce(node.type, 'CodeNode') AS kind,
                node.keywordTextChecksum AS keywordTextChecksum,
                {
                  name: node.name,
                  summary: node.summary,
                  namespace: node.namespace,
                  filePath: node.filePath,
                  type: node.type
                } AS textBySource,
                coalesce(node.updatedAt, node.createdAt, 0) AS sortValue
              UNION ALL
              MATCH (document:KnowledgeDocument)
              WHERE ($projectContextNormalized IS NULL OR document.projectContextNormalized = $projectContextNormalized)
              RETURN
                document.id AS id,
                document.projectContext AS projectContext,
                'KnowledgeDocument' AS kind,
                document.keywordTextChecksum AS keywordTextChecksum,
                {
                  title: document.source,
                  content: document.content,
                  source: document.source,
                  kind: document.metadataKind
                } AS textBySource,
                coalesce(document.updatedAt, document.createdAt, 0) AS sortValue
            }
            RETURN id, projectContext, kind, keywordTextChecksum, textBySource
            ORDER BY sortValue DESC, id
            SKIP $skip
            LIMIT $take
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(query.ProjectContext),
            skip = query.Skip,
            take = query.Take
        });

        var results = new List<KeywordSourceNode>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapKeywordSourceNode(record));

        return results;
    }

    public async Task<IReadOnlyList<KeywordSourceNode>> GetKeywordSourceNodesByIdAsync(
        IReadOnlyCollection<string> sourceNodeIds,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        if (sourceNodeIds.Count == 0)
            return [];

        await using var session = _driver.AsyncSession();

        const string cypher = """
            CALL {
              MATCH (node:CodeNode)
              WHERE node.id IN $sourceNodeIds
                AND ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
              RETURN
                node.id AS id,
                node.projectContext AS projectContext,
                coalesce(node.type, 'CodeNode') AS kind,
                node.keywordTextChecksum AS keywordTextChecksum,
                {
                  name: node.name,
                  summary: node.summary,
                  namespace: node.namespace,
                  filePath: node.filePath,
                  type: node.type
                } AS textBySource
              UNION ALL
              MATCH (document:KnowledgeDocument)
              WHERE document.id IN $sourceNodeIds
                AND ($projectContextNormalized IS NULL OR document.projectContextNormalized = $projectContextNormalized)
              RETURN
                document.id AS id,
                document.projectContext AS projectContext,
                'KnowledgeDocument' AS kind,
                document.keywordTextChecksum AS keywordTextChecksum,
                {
                  title: document.source,
                  content: document.content,
                  source: document.source,
                  kind: document.metadataKind
                } AS textBySource
            }
            RETURN id, projectContext, kind, keywordTextChecksum, textBySource
            ORDER BY id
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            sourceNodeIds = sourceNodeIds.Distinct(StringComparer.Ordinal).ToArray(),
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var results = new List<KeywordSourceNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapKeywordSourceNode(record));

        return results;
    }

    public async Task ReplaceKeywordsAsync(
        ReplaceKeywordRelationshipsCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await session.ExecuteWriteAsync(async tx =>
        {
            var deleteCursor = await tx.RunAsync(
                """
                MATCH (source {id: $sourceNodeId})-[relationship:HAS_KEYWORD]->(:Keyword)
                DELETE relationship
                """,
                new { sourceNodeId = command.SourceNodeId });
            await deleteCursor.ConsumeAsync();

            var metadataCursor = await tx.RunAsync(
                """
                MATCH (source {id: $sourceNodeId})
                SET source.keywordTextChecksum = $keywordTextChecksum,
                    source.keywordIndexedAt = $now,
                    source.keywordEnrichmentVersion = $enrichmentVersion
                """,
                new
                {
                    sourceNodeId = command.SourceNodeId,
                    keywordTextChecksum = command.KeywordTextChecksum,
                    enrichmentVersion = command.EnrichmentVersion,
                    now
                });
            await metadataCursor.ConsumeAsync();

            if (command.Keywords.Count == 0)
                return;

            var relationshipCursor = await tx.RunAsync(
                """
                MATCH (source {id: $sourceNodeId})
                WITH source, source.projectContext AS projectContext, source.projectContextNormalized AS projectContextNormalized
                UNWIND $keywords AS keyword
                MERGE (k:Keyword {projectContextNormalized: projectContextNormalized, normalizedValue: keyword.normalizedValue})
                ON CREATE SET k.createdAt = $now
                SET k.id = 'keyword:' + coalesce(projectContextNormalized, 'all-projects') + ':' + keyword.normalizedValue,
                    k.projectContext = projectContext,
                    k.value = keyword.value,
                    k.updatedAt = $now
                MERGE (source)-[relationship:HAS_KEYWORD]->(k)
                SET relationship.count = keyword.count,
                    relationship.weight = keyword.weight,
                    relationship.source = keyword.source,
                    relationship.indexedAt = $now,
                    relationship.enrichmentVersion = $enrichmentVersion
                """,
                new
                {
                    sourceNodeId = command.SourceNodeId,
                    enrichmentVersion = command.EnrichmentVersion,
                    now,
                    keywords = command.Keywords.Select(keyword => new
                    {
                        value = keyword.Value,
                        normalizedValue = keyword.NormalizedValue,
                        count = keyword.Count,
                        weight = keyword.Weight,
                        source = string.Join("|", keyword.Sources)
                    }).ToArray()
                });
            await relationshipCursor.ConsumeAsync();
        });
    }

    public async Task RecalculateKeywordStatisticsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await session.ExecuteWriteAsync(async tx =>
        {
            var statsCursor = await tx.RunAsync(
                """
                MATCH (k:Keyword)
                WHERE ($projectContextNormalized IS NULL OR k.projectContextNormalized = $projectContextNormalized)
                OPTIONAL MATCH (source)-[relationship:HAS_KEYWORD]->(k)
                WITH k, count(DISTINCT source) AS documentFrequency, coalesce(sum(relationship.count), 0) AS totalFrequency
                SET k.documentFrequency = documentFrequency,
                    k.totalFrequency = totalFrequency,
                    k.updatedAt = $now
                """,
                new
                {
                    projectContextNormalized = (object?)Normalize(projectContext),
                    now
                });
            await statsCursor.ConsumeAsync();

            var orphanCursor = await tx.RunAsync(
                """
                MATCH (k:Keyword)
                WHERE ($projectContextNormalized IS NULL OR k.projectContextNormalized = $projectContextNormalized)
                  AND NOT EXISTS { MATCH ()-[:HAS_KEYWORD]->(k) }
                DETACH DELETE k
                """,
                new { projectContextNormalized = (object?)Normalize(projectContext) });
            await orphanCursor.ConsumeAsync();
        });
    }

    public async Task<int> GetKeywordSourceNodeCountAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            CALL {
              MATCH (node:CodeNode)
              WHERE ($projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
              RETURN count(node) AS count
              UNION ALL
              MATCH (document:KnowledgeDocument)
              WHERE ($projectContextNormalized IS NULL OR document.projectContextNormalized = $projectContextNormalized)
              RETURN count(document) AS count
            }
            RETURN coalesce(sum(count), 0) AS totalCount
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var record = await cursor.SingleAsync();
        return record["totalCount"].As<int>();
    }

    public async Task<IReadOnlyList<KeywordForClassification>> GetKeywordsForClassificationAsync(
        string? projectContext,
        int classificationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (keyword:Keyword)
            WHERE ($projectContextNormalized IS NULL OR keyword.projectContextNormalized = $projectContextNormalized)
              AND coalesce(keyword.classificationVersion, 0) < $classificationVersion
            SET keyword.id = coalesce(
              keyword.id,
              'keyword:' + coalesce(keyword.projectContextNormalized, 'all-projects') + ':' + keyword.normalizedValue)
            RETURN
              keyword.id AS id,
              keyword.normalizedValue AS normalizedValue,
              coalesce(keyword.documentFrequency, 0) AS documentFrequency,
              coalesce(keyword.totalFrequency, 0) AS totalFrequency
            ORDER BY keyword.normalizedValue
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
            classificationVersion
        });

        var results = new List<KeywordForClassification>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(new KeywordForClassification
            {
                Id = record["id"].As<string>(),
                NormalizedValue = record["normalizedValue"].As<string>(),
                DocumentFrequency = record["documentFrequency"].As<int>(),
                TotalFrequency = record["totalFrequency"].As<int>()
            });
        }

        return results;
    }

    public async Task SaveKeywordClassificationsAsync(
        IReadOnlyCollection<KeywordClassificationResult> results,
        int classificationVersion,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
            return;

        await using var session = _driver.AsyncSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                UNWIND $results AS result
                MATCH (keyword:Keyword {id: result.keywordId})
                SET keyword.classification = result.classification,
                    keyword.isCommon = result.isCommon,
                    keyword.isNoise = result.isNoise,
                    keyword.usefulnessScore = result.usefulnessScore,
                    keyword.classifiedAt = $now,
                    keyword.classificationVersion = $classificationVersion,
                    keyword.updatedAt = $now
                """,
                new
                {
                    classificationVersion,
                    now,
                    results = results.Select(result => new
                    {
                        keywordId = result.KeywordId,
                        classification = result.Classification.ToString(),
                        isCommon = result.IsCommon,
                        isNoise = result.IsNoise,
                        usefulnessScore = result.UsefulnessScore
                    }).ToArray()
                });
            await cursor.ConsumeAsync();
        });
    }

    public async Task<IReadOnlyList<KeywordRelatedNode>> FindRelatedByKeywordsAsync(
        KeywordRelatedNodeQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (source {id: $sourceNodeId})
            CALL {
              WITH source
              MATCH (projectNode)
              WHERE (projectNode:CodeNode OR projectNode:KnowledgeDocument)
                AND (source.projectContextNormalized IS NULL OR projectNode.projectContextNormalized = source.projectContextNormalized)
              RETURN count(projectNode) AS projectNodeCount
            }
            MATCH (source)-[sk:HAS_KEYWORD]->(keyword:Keyword)<-[tk:HAS_KEYWORD]-(target)
            WHERE target.id <> source.id
              AND coalesce(keyword.isNoise, false) = false
              AND (
                size($targetKinds) = 0
                OR CASE
                     WHEN target:KnowledgeDocument THEN 'KnowledgeDocument'
                     ELSE coalesce(target.type, 'CodeNode')
                   END IN $targetKinds
              )
              AND keyword.documentFrequency <= CASE
                    WHEN projectNodeCount <= 0 THEN 1
                    ELSE toInteger(ceil(projectNodeCount * $maximumDocumentFrequencyRatio))
                  END
            WITH
              target,
              count(DISTINCT keyword) AS sharedKeywordCount,
              sum(sk.weight * tk.weight * coalesce(keyword.usefulnessScore, 1.0)) AS score,
              collect(DISTINCT keyword.normalizedValue)[0..20] AS matchedKeywords
            WHERE sharedKeywordCount >= $minimumSharedKeywords
              AND score >= $minimumScore
            RETURN
              target.id AS targetNodeId,
              CASE
                WHEN target:KnowledgeDocument THEN 'KnowledgeDocument'
                ELSE coalesce(target.type, 'CodeNode')
              END AS targetKind,
              sharedKeywordCount,
              score,
              matchedKeywords
            ORDER BY score DESC, sharedKeywordCount DESC, targetNodeId
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            sourceNodeId = query.SourceNodeId,
            targetKinds = query.TargetKinds.ToArray(),
            minimumSharedKeywords = query.MinimumSharedKeywords,
            minimumScore = query.MinimumScore,
            maximumDocumentFrequencyRatio = query.MaximumDocumentFrequencyRatio,
            limit = query.Limit
        });

        var results = new List<KeywordRelatedNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(new KeywordRelatedNode
            {
                TargetNodeId = record["targetNodeId"].As<string>(),
                TargetKind = record["targetKind"].As<string>(),
                SharedKeywordCount = record["sharedKeywordCount"].As<int>(),
                Score = Math.Round(record["score"].As<double>(), 4, MidpointRounding.AwayFromZero),
                MatchedKeywords = record["matchedKeywords"].As<List<string>>()
            });
        }

        return results;
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();

    private static KeywordSourceNode MapKeywordSourceNode(IRecord record)
    {
        var rawMap = record["textBySource"].As<IDictionary<string, object?>>();
        var textBySource = rawMap.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value?.ToString(),
            StringComparer.Ordinal);

        return new KeywordSourceNode
        {
            Id = record["id"].As<string>(),
            ProjectContext = record["projectContext"].As<string?>(),
            Kind = record["kind"].As<string>(),
            ExistingChecksum = record["keywordTextChecksum"].As<string?>(),
            TextBySource = textBySource
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
