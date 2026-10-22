using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private const string DefaultArchitectureFilePath = ".meridian/architecture.json";

    private static readonly ArchitectureRuleSet DefaultArchitectureRules = new(
        "Clean Architecture",
        [
            new ArchitectureLayerDefinition("Core", [".Core", ".Domain"]),
            new ArchitectureLayerDefinition("Application", [".Application"]),
            new ArchitectureLayerDefinition("Infrastructure", [".Infrastructure", ".Persistence"]),
            new ArchitectureLayerDefinition("Presentation", [".Api", ".Web", ".UI", ".McpServer"])
        ],
        [
            new ArchitectureForbiddenDependencyRule("Core", "Application", "Core must not depend on Application"),
            new ArchitectureForbiddenDependencyRule("Core", "Infrastructure", "Core must not depend on Infrastructure"),
            new ArchitectureForbiddenDependencyRule("Core", "Presentation", "Core must not depend on Presentation"),
            new ArchitectureForbiddenDependencyRule("Application", "Infrastructure", "Application must not depend on Infrastructure"),
            new ArchitectureForbiddenDependencyRule("Application", "Presentation", "Application must not depend on Presentation")
        ]);

    private async Task<ArchitectureRuleSet> LoadArchitectureRulesAsync(
        IAsyncSession session,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var configuredPath = await LoadActiveArchitecturePathAsync(session, projectContext, cancellationToken);
        var (exists, entries) = await LoadConfigurationEntriesForFileAsync(session,
            configuredPath ?? DefaultArchitectureFilePath, projectContext, cancellationToken);
        if (!exists && configuredPath is null) return DefaultArchitectureRules;
        return ArchitectureRuleSetConfiguration.ParseEntries(entries);
    }

    private async Task<string?> LoadActiveArchitecturePathAsync(
        IAsyncSession session,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        const string cypher = """
            MATCH (key:CodeNode)
            WHERE key.type = 'ConfigurationKey'
              AND key.nameNormalized = 'architecture:path'
              AND ($projectContextNormalized IS NULL OR key.projectContextNormalized = $projectContextNormalized)
            MATCH (entry:CodeNode)-[keyRel:DefinesConfig|OverridesConfig]->(key)
            MATCH (file:CodeNode)-[:DefinesConfig]->(entry)
            WHERE file.type = 'ConfigurationFile'
            RETURN coalesce(keyRel.valuePreview, entry.rawValuePreview) AS valuePreview,
                   file.filePath AS filePath,
                   CASE WHEN file.filePathNormalized = 'meridian.json' THEN 0 ELSE 1 END AS fileRank,
                   CASE WHEN type(keyRel) = 'OverridesConfig' THEN 0 ELSE 1 END AS relationshipRank
            ORDER BY relationshipRank ASC, fileRank ASC, file.filePath ASC
            LIMIT 1
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var value = record["valuePreview"].As<string?>();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Replace('\\', '/');
        }

        return null;
    }

    private async Task<(bool Exists, IReadOnlyDictionary<string, string> Entries)> LoadConfigurationEntriesForFileAsync(
        IAsyncSession session,
        string relativePath,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        const string cypher = """
            MATCH (file:CodeNode)
            WHERE file.type = 'ConfigurationFile'
              AND file.filePathNormalized = $filePathNormalized
              AND ($projectContextNormalized IS NULL OR file.projectContextNormalized = $projectContextNormalized)
            OPTIONAL MATCH (file)-[:DefinesConfig]->(entry:CodeNode)
            WHERE entry.type = 'ConfigurationEntry'
            RETURN coalesce(entry.canonicalKey, entry.name) AS canonicalKey,
                   coalesce(entry.rawValuePreview, '') AS valuePreview
            ORDER BY canonicalKey
            LIMIT 1001
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            filePathNormalized = Normalize(relativePath),
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exists = false;
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            exists = true;
            var key = record["canonicalKey"].As<string?>();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (entries.Count >= 1000 || !entries.TryAdd(key, record["valuePreview"].As<string?>() ?? string.Empty))
                throw new ArgumentException("Architecture configuration entries exceed their limit or contain duplicate keys.");
        }

        return (exists, entries);
    }

    private static (string WhereClause, string CaseClause, Dictionary<string, object?> Parameters) BuildArchitectureRuleCypher(
        ArchitectureRuleSet architecture)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var layers = architecture.Layers.ToDictionary(layer => layer.Id, StringComparer.OrdinalIgnoreCase);
        var whereBranches = new List<string>();
        var caseBranches = new List<string>();
        var ruleIndex = 0;

        foreach (var rule in architecture.ForbiddenDependencies)
        {
            if (!layers.TryGetValue(rule.FromLayerId, out var sourceLayer)
                || !layers.TryGetValue(rule.ToLayerId, out var targetLayer)
                || sourceLayer.NamespaceContainsAny.Count == 0
                || targetLayer.NamespaceContainsAny.Count == 0)
            {
                continue;
            }

            var sourceParam = $"sourcePatterns{ruleIndex}";
            var targetParam = $"targetPatterns{ruleIndex}";
            var reasonParam = $"ruleReason{ruleIndex}";
            var branch = $"(any(token IN ${sourceParam} WHERE source.namespace CONTAINS token) AND any(token IN ${targetParam} WHERE target.namespace CONTAINS token))";

            parameters[sourceParam] = sourceLayer.NamespaceContainsAny.ToArray();
            parameters[targetParam] = targetLayer.NamespaceContainsAny.ToArray();
            parameters[reasonParam] = rule.Reason;
            whereBranches.Add(branch);
            caseBranches.Add($"WHEN {branch} THEN ${reasonParam}");
            ruleIndex++;
        }

        if (whereBranches.Count == 0)
        {
            return (
                "false",
                "WHEN false THEN 'No architecture rules configured'",
                parameters);
        }

        return (
            string.Join(" OR ", whereBranches),
            string.Join(Environment.NewLine + "                 ", caseBranches),
            parameters);
    }
}
