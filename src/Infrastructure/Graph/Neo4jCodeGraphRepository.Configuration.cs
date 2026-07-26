using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<IReadOnlyList<ConfigurationDefinition>> FindConfigDefinitionsAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (key:CodeNode)
            WHERE key.type = 'ConfigurationKey'
              AND key.nameNormalized = $canonicalKeyNormalized
              AND ($projectContextNormalized IS NULL OR key.projectContextNormalized = $projectContextNormalized)
            MATCH (entry:CodeNode)-[keyRel:DefinesConfig|OverridesConfig]->(key)
            WHERE entry.type = 'ConfigurationEntry'
            MATCH (file:CodeNode)-[:DefinesConfig]->(entry)
            WHERE file.type = 'ConfigurationFile'
            RETURN file, entry, key, type(keyRel) AS relationshipType,
                   coalesce(keyRel.rawKey, entry.rawKey) AS rawKey,
                   coalesce(keyRel.sourceKind, entry.sourceKind) AS sourceKind,
                   coalesce(keyRel.valuePreview, entry.rawValuePreview) AS valuePreview
            ORDER BY file.filePath, entry.name
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            canonicalKeyNormalized = Normalize(canonicalKey),
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var results = new List<ConfigurationDefinition>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(new ConfigurationDefinition
            {
                FileNode = MapToCodeNode(record["file"].As<INode>()),
                EntryNode = MapToCodeNode(record["entry"].As<INode>()),
                KeyNode = MapToCodeNode(record["key"].As<INode>()),
                RelationshipType = record["relationshipType"].As<string>(),
                RawKey = record["rawKey"].As<string?>(),
                SourceKind = record["sourceKind"].As<string?>(),
                ValuePreview = record["valuePreview"].As<string?>()
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<ConfigurationUsage>> FindConfigUsageAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (consumer:CodeNode)-[rel:ReadsConfig|BindsConfig]->(key:CodeNode)
            WHERE key.type = 'ConfigurationKey'
              AND (
                  key.nameNormalized = $canonicalKeyNormalized
                  OR (
                      size($sectionKeysNormalized) > 0
                      AND type(rel) = 'BindsConfig'
                      AND key.nameNormalized IN $sectionKeysNormalized
                  )
              )
              AND ($projectContextNormalized IS NULL
                   OR consumer.projectContextNormalized = $projectContextNormalized
                   OR key.projectContextNormalized = $projectContextNormalized)
            RETURN consumer, key, type(rel) AS relationshipType,
                   rel.rawKey AS rawKey,
                   CASE
                       WHEN key.nameNormalized <> $canonicalKeyNormalized
                       THEN coalesce(rel.accessPattern, 'section binding') + ' (covers requested leaf)'
                       ELSE rel.accessPattern
                   END AS accessPattern,
                   rel.optionsType AS optionsType,
                   CASE
                       WHEN key.nameNormalized <> $canonicalKeyNormalized
                       THEN CASE
                           WHEN coalesce(rel.confidence, 0.75) > 0.75 THEN 0.75
                           ELSE coalesce(rel.confidence, 0.75)
                       END
                       ELSE rel.confidence
                   END AS confidence
            ORDER BY coalesce(rel.confidence, 0) DESC, consumer.filePath, consumer.lineNumber, consumer.name
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            canonicalKeyNormalized = Normalize(canonicalKey),
            sectionKeysNormalized = GetParentConfigurationKeys(canonicalKey)
                .Select(Normalize)
                .ToArray(),
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var results = new List<ConfigurationUsage>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(new ConfigurationUsage
            {
                ConsumerNode = MapToCodeNode(record["consumer"].As<INode>()),
                KeyNode = MapToCodeNode(record["key"].As<INode>()),
                RelationshipType = record["relationshipType"].As<string>(),
                RawKey = record["rawKey"].As<string?>(),
                AccessPattern = record["accessPattern"].As<string?>(),
                OptionsType = record["optionsType"].As<string?>(),
                Confidence = record["confidence"].As<double?>()
            });
        }

        return results;
    }

    private static IReadOnlyList<string> GetParentConfigurationKeys(string canonicalKey)
    {
        var segments = canonicalKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return [];

        return Enumerable.Range(1, segments.Length - 1)
            .Select(length => string.Join(':', segments.Take(length)))
            .ToArray();
    }
}
