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
        var activeArchitecturePath = await LoadActiveArchitecturePathAsync(session, projectContext, cancellationToken)
            ?? DefaultArchitectureFilePath;
        var entries = await LoadConfigurationEntriesForFileAsync(session, activeArchitecturePath, projectContext, cancellationToken);
        return ParseArchitectureRuleSet(entries) ?? DefaultArchitectureRules;
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

    private async Task<IReadOnlyDictionary<string, string>> LoadConfigurationEntriesForFileAsync(
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
            MATCH (file)-[:DefinesConfig]->(entry:CodeNode)
            WHERE entry.type = 'ConfigurationEntry'
            RETURN coalesce(entry.canonicalKey, entry.name) AS canonicalKey,
                   coalesce(entry.rawValuePreview, '') AS valuePreview
            ORDER BY canonicalKey
            LIMIT 500
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            filePathNormalized = Normalize(relativePath),
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var key = record["canonicalKey"].As<string?>();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            entries[key] = record["valuePreview"].As<string?>() ?? string.Empty;
        }

        return entries;
    }

    private static ArchitectureRuleSet? ParseArchitectureRuleSet(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0)
            return null;

        var name = entries.TryGetValue("name", out var configuredName) && !string.IsNullOrWhiteSpace(configuredName)
            ? configuredName
            : "Configured Architecture";

        var layerEntries = GroupIndexedEntries(entries, "layers");
        var layers = layerEntries
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                var id = entry.Value.TryGetValue("id", out var layerId) ? layerId : string.Empty;
                var patterns = CollectIndexedValues(entry.Value, "namespaceContainsAny");
                return string.IsNullOrWhiteSpace(id) || patterns.Count == 0
                    ? null
                    : new ArchitectureLayerDefinition(id, patterns);
            })
            .Where(layer => layer is not null)
            .Cast<ArchitectureLayerDefinition>()
            .ToArray();

        if (layers.Length == 0)
            return null;

        var layersById = layers.ToDictionary(layer => layer.Id, StringComparer.OrdinalIgnoreCase);
        var ruleEntries = GroupIndexedEntries(entries, "forbiddenDependencies");
        var rules = ruleEntries
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                var values = entry.Value;
                var from = values.TryGetValue("from", out var fromLayer) ? fromLayer : string.Empty;
                var to = values.TryGetValue("to", out var toLayer) ? toLayer : string.Empty;
                var reason = values.TryGetValue("reason", out var configuredReason) && !string.IsNullOrWhiteSpace(configuredReason)
                    ? configuredReason
                    : $"{from} must not depend on {to}";

                return !layersById.ContainsKey(from) || !layersById.ContainsKey(to)
                    ? null
                    : new ArchitectureForbiddenDependencyRule(from, to, reason);
            })
            .Where(rule => rule is not null)
            .Cast<ArchitectureForbiddenDependencyRule>()
            .ToArray();

        return rules.Length == 0
            ? null
            : new ArchitectureRuleSet(name, layers, rules);
    }

    private static Dictionary<int, Dictionary<string, string>> GroupIndexedEntries(
        IReadOnlyDictionary<string, string> entries,
        string rootKey)
    {
        var groups = new Dictionary<int, Dictionary<string, string>>();
        var prefix = $"{rootKey}:";

        foreach (var (key, value) in entries)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = key[prefix.Length..];
            var separatorIndex = remainder.IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            if (!int.TryParse(remainder[..separatorIndex], out var index))
                continue;

            var childKey = remainder[(separatorIndex + 1)..];
            if (!groups.TryGetValue(index, out var group))
            {
                group = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                groups[index] = group;
            }

            group[childKey] = value;
        }

        return groups;
    }

    private static List<string> CollectIndexedValues(
        IReadOnlyDictionary<string, string> values,
        string keyPrefix)
    {
        return values
            .Where(entry => entry.Key.StartsWith($"{keyPrefix}:", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var suffix = entry.Key[(keyPrefix.Length + 1)..];
                return int.TryParse(suffix, out var index)
                    ? (Index: index, Value: entry.Value)
                    : (Index: int.MaxValue, Value: entry.Value);
            })
            .OrderBy(entry => entry.Index)
            .Select(entry => entry.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
