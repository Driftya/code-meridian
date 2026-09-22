using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    private const double RelationshipFailureLowConfidenceRate = 0.05;

    private async Task<RelationshipTrust> GetRelationshipTrustAsync(
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var persistedEvidence = (await codeGraph.CountEdgeEvidenceAsync(projectContext, cancellationToken))?
            .ToDictionary(item => item.Key.ToString().ToLowerInvariant(), item => item.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, long>(StringComparer.Ordinal);
        var nativeRuns = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                TypeFilter = CodeNodeType.IndexRun,
                ProjectContext = projectContext,
                Limit = 500
            },
            cancellationToken);
        var compatibleRuns = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                TypeFilter = CodeNodeType.Diagnostic,
                NameFilter = "index run",
                ProjectContext = projectContext,
                Limit = 200
            },
            cancellationToken);
        var scopeCatalogs = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                TypeFilter = CodeNodeType.Diagnostic,
                NameFilter = "relationship scope catalog",
                ProjectContext = projectContext,
                Limit = 20
            },
            cancellationToken);
        var parsedRuns = nativeRuns
            .Concat(compatibleRuns)
            .Where(IsRelationshipIndexRun)
            .DistinctBy(node => node.Id)
            .Select(ParseIndexRun)
            .OrderByDescending(run => run.Timestamp)
            .ToArray();
        var scopesByProject = scopeCatalogs.GroupBy(node => node.ProjectContext ?? string.Empty)
            .ToDictionary(group => group.Key, group => ReadActiveTypeScriptScopes(group));
        parsedRuns = parsedRuns
            .Where(run => !string.Equals(run.Language, "TypeScript", StringComparison.OrdinalIgnoreCase)
                || !scopesByProject.TryGetValue(run.ProjectContext, out var scopes)
                || scopes is null
                || scopes.Contains(NormalizeResolutionScope(run.ResolutionScope)))
            .ToArray();

        if (parsedRuns.Length == 0)
        {
            return new RelationshipTrust(
                "Unknown",
                "no index-run relationship statistics are available",
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                [],
                persistedEvidence);
        }

        var currentRuns = parsedRuns
            .GroupBy(run => (run.ProjectContext, Language: run.Language.ToUpperInvariant(),
                Scope: NormalizeResolutionScope(run.ResolutionScope)))
            .Select(group => group.First())
            .ToArray();
        var warnings = new List<string>();
        var confidence = "High";
        var externalCount = currentRuns.Sum(run => run.ExternalOrUnindexedCount);
        var unresolvedLocalCount = currentRuns.Sum(run => run.UnresolvedLocalCount);
        var indeterminateCount = currentRuns.Sum(run => run.IndeterminateCount);
        var legacyEstimate = currentRuns.Sum(run => run.LegacyUnresolvedEstimate);
        var duplicateCount = currentRuns.Sum(run => run.DuplicateCount);
        var syntheticCount = currentRuns.Sum(run => run.SyntheticCount);
        var evidenceCounts = currentRuns.SelectMany(run => run.EdgeEvidenceCounts)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal);

        foreach (var run in currentRuns)
        {
            var scope = $"{run.ProjectContext}/{run.Language}/{run.ResolutionScope}";
            if (!run.UsedFullResolutionCatalog)
            {
                confidence = "Low";
                warnings.Add($"{scope} used a partial relationship-resolution catalog");
            }

            if (run.SchemaVersion >= 2)
            {
                // External dependencies are outside the indexed graph, not successful local resolutions.
                // Unknown provenance is reported separately; runtime callbacks are not proven missing locals.
                var localCandidates = Math.Max(run.UnresolvedLocalCount,
                    run.AttemptedCalls + run.AttemptedReferences - run.ExternalOrUnindexedCount - run.IndeterminateCount);
                if (run.UnresolvedLocalCount > 0)
                {
                    if ((double)run.UnresolvedLocalCount / localCandidates >= RelationshipFailureLowConfidenceRate)
                        confidence = "Low";
                    else if (confidence == "High")
                        confidence = "Medium";
                }
                if (run.UnresolvedLocalCount > 0)
                {
                    warnings.Add($"{scope} reported {run.UnresolvedLocalCount} unresolved local relationship(s)"
                        + $" ({(run.UnresolvedLocalCount * 100d / localCandidates).ToString("0.0", CultureInfo.InvariantCulture)}% of local candidates)");
                }

                if (run.IndeterminateCount > 0)
                {
                    if (confidence == "High")
                        confidence = "Medium";
                    warnings.Add($"{scope} reported {run.IndeterminateCount} relationship(s) with indeterminate provenance");
                }
            }
            else if (run.LegacyUnresolvedEstimate > 0)
            {
                if (confidence == "High")
                    confidence = "Medium";
                warnings.Add($"{scope} has a legacy unresolved estimate of {run.LegacyUnresolvedEstimate}; external and local failures were not distinguished");
            }

            AppendTopReasonWarning(warnings, scope, "call", run.CallReasons, run.AttemptedCalls);
            AppendTopReasonWarning(warnings, scope, "reference", run.ReferenceReasons, run.AttemptedReferences);

            // Incremental counts may describe only the emitted batch even with a full resolution
            // catalog. They cannot be compared to full-index counts as evidence of lost edges.
        }

        var evidence = $"classified {externalCount} relationship(s) as external or outside the indexed scope";
        if (persistedEvidence.Count > 0)
            warnings.Add("persisted edge evidence: " + string.Join(", ", persistedEvidence
                .OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}")));
        if (evidenceCounts.Count > 0)
            warnings.Add("emitted edge evidence: " + string.Join(", ", evidenceCounts
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value}")));
        if (warnings.Count == 0)
            warnings.Add($"the latest run for each language/scope used a full catalog and reported no actionable relationship failures; {evidence}");
        else
            warnings.Add(evidence);

        var samples = currentRuns
            .SelectMany(run => run.Samples)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        return new RelationshipTrust(
            confidence,
            "Project/scope index health, not confidence in individual returned edges: "
                + string.Join("; ", warnings.Take(6))
                + (warnings.Count > 6 ? $"; {warnings.Count - 6} further scope details omitted" : string.Empty)
                + (confidence != "High" && samples.Length > 0 ? $"; sample: {samples[0]}" : string.Empty),
            parsedRuns.Where(run => run.Mode == "full").MaxBy(run => run.Timestamp)?.Timestamp,
            parsedRuns.Where(run => run.Mode == "incremental").MaxBy(run => run.Timestamp)?.Timestamp,
            externalCount,
            unresolvedLocalCount + legacyEstimate,
            indeterminateCount,
            duplicateCount,
            syntheticCount,
            samples,
            persistedEvidence);
    }

    private static void AppendRelationshipTrustWarning(StringBuilder builder, RelationshipTrust trust)
    {
        if (trust.Confidence == "High")
            return;

        builder.AppendLine($"> Relationship completeness: **{trust.Confidence}** — {trust.Reason}. Empty relationship results are not proof that a change is safe.");
        builder.AppendLine();
    }

    private static string RelationshipTrustWarning(RelationshipTrust trust) =>
        trust.Confidence == "High"
            ? string.Empty
            : $" Relationship completeness is {trust.Confidence.ToLowerInvariant()}: {trust.Reason}. An empty relationship result is not proof that a change is safe.";

    private static void AppendRelationshipEvidence(StringBuilder builder, RelationshipTrust trust)
    {
        if (trust.Confidence == "Unknown" && trust.EvidenceCounts.Count == 0)
            return;

        if (trust.Confidence != "Unknown")
            builder.AppendLine(
                $"**Relationship outcomes:** {trust.UnresolvedLocalCount} unresolved local, "
                + $"{trust.IndeterminateCount} indeterminate, {trust.ExternalOrUnindexedCount} external/unindexed, "
                + $"{trust.DuplicateCount} duplicate candidate(s), {trust.SyntheticCount} synthetic edge(s)");
        if (trust.Samples.Count > 0)
            builder.AppendLine($"**Relationship failure samples:** {string.Join("; ", trust.Samples)}");
        if (trust.EvidenceCounts.Count > 0)
            builder.AppendLine($"**Persisted edge evidence:** {string.Join(", ", trust.EvidenceCounts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))}");
    }

    private static RelationshipCompletenessResult ToResult(RelationshipTrust trust) =>
        new(
            trust.Confidence,
            trust.Reason,
            trust.LastFullIndex,
            trust.LastIncrementalIndex,
            trust.ExternalOrUnindexedCount,
            trust.UnresolvedLocalCount,
            trust.IndeterminateCount,
            trust.DuplicateCount,
            trust.SyntheticCount,
            trust.Samples,
            trust.EvidenceCounts);

    private static ParsedIndexRun ParseIndexRun(CodeNode node)
    {
        var properties = node.Properties;
        var mode = Read("mode") ?? (node.Name.StartsWith("incremental", StringComparison.OrdinalIgnoreCase) ? "incremental" : "full");
        var attempted = ReadInt("attemptedCallEdges") + ReadInt("attemptedReferenceEdges");
        var attemptedCalls = ReadInt("attemptedCallEdges");
        var attemptedReferences = ReadInt("attemptedReferenceEdges");
        var resolved = ReadInt("resolvedCallEdges") + ReadInt("resolvedReferenceEdges");
        var schemaVersion = ReadInt("relationshipHealthSchemaVersion");
        return new ParsedIndexRun(
            node.ProjectContext ?? string.Empty,
            mode,
            Read("language") ?? "CSharp",
            Read("resolutionScope") ?? "project",
            schemaVersion,
            ReadBool("usedFullResolutionCatalog"),
            ReadInt("scannedFileCount"),
            ReadInt("ingestedFileCount"),
            resolved,
            attemptedCalls,
            attemptedReferences,
            ReadInt("externalOrUnindexedRelationshipCount"),
            ReadInt("unresolvedLocalRelationshipCount"),
            ReadInt("indeterminateRelationshipCount"),
            ReadInt("duplicateRelationshipCount"),
            ReadInt("syntheticRelationshipCount"),
            schemaVersion >= 2 ? 0 : Math.Max(0, attempted - resolved),
            ReadReasonCounts(Read("callRelationshipOutcomes")),
            ReadReasonCounts(Read("referenceRelationshipOutcomes")),
            ReadEdgeEvidenceCounts(Read("edgeEvidenceCounts")),
            ReadSamples(Read("relationshipFailureSamples")),
            node.LastIndexedAt ?? node.UpdatedAt ?? node.CreatedAt);

        string? Read(string key) => properties.TryGetValue(key, out var value) ? value : null;
        int ReadInt(string key) => int.TryParse(Read(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        bool ReadBool(string key) => bool.TryParse(Read(key), out var value) && value;
    }

    private static IReadOnlyList<string> ReadSamples(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return document.RootElement.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.Object)
                .Select(element =>
                {
                    var reason = ReadJsonString(element, "Reason") ?? ReadJsonString(element, "reason") ?? "unknown reason";
                    var file = ReadJsonString(element, "FilePath") ?? ReadJsonString(element, "filePath");
                    var fileRole = ReadJsonString(element, "FileRole") ?? ReadJsonString(element, "fileRole");
                    var receiverShape = ReadJsonString(element, "ReceiverKind")
                        ?? ReadJsonString(element, "receiverKind")
                        ?? ReadJsonString(element, "receiverShape");
                    var evidenceKind = ReadJsonString(element, "EvidenceKind") ?? ReadJsonString(element, "evidenceKind");
                    var resolver = ReadJsonString(element, "Resolver") ?? ReadJsonString(element, "resolver");
                    var line = ReadJsonInt(element, "LineNumber") ?? ReadJsonInt(element, "lineNumber");
                    var rolePrefix = fileRole switch
                    {
                        "Source" => "production ",
                        "Test" => "test ",
                        { Length: > 0 } => $"{fileRole.ToLowerInvariant()} ",
                        _ => string.Empty
                    };
                    var detail = reason
                        + (evidenceKind is null ? string.Empty : $"; {evidenceKind}")
                        + (resolver is null ? string.Empty : $"; {resolver}")
                        + (receiverShape is null ? string.Empty : $"; receiver={receiverShape}");
                    return string.IsNullOrWhiteSpace(file)
                        ? detail
                        : $"{rolePrefix}{file}{(line is > 0 ? $":{line}" : "")} ({detail})";
                })
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<string, int> ReadReasonCounts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (!root.TryGetProperty("Reasons", out var reasons)
                    && !root.TryGetProperty("reasons", out reasons))
                || reasons.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, int>(StringComparer.Ordinal);
            }

            return reasons.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out _))
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.GetInt32(),
                    StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, int> ReadEdgeEvidenceCounts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int>();
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject()
                    .Where(item => item.Value.ValueKind == JsonValueKind.Number
                        && item.Value.TryGetInt32(out var value) && value >= 0)
                    .ToDictionary(item => item.Name, item => item.Value.GetInt32(), StringComparer.Ordinal)
                : new Dictionary<string, int>();
        }
        catch (JsonException) { return new Dictionary<string, int>(); }
    }

    private static void AppendTopReasonWarning(
        ICollection<string> warnings,
        string scope,
        string edgeKind,
        IReadOnlyDictionary<string, int> reasons,
        int attempted)
    {
        var topReasons = reasons
            .Where(item => item.Value > 0 && !item.Key.StartsWith("external_or_unindexed:", StringComparison.Ordinal))
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(item => attempted > 0
                ? $"{item.Key}={item.Value} ({(item.Value * 100d / attempted).ToString("0.0", CultureInfo.InvariantCulture)}% of {edgeKind}s)"
                : $"{item.Key}={item.Value}")
            .ToArray();
        if (topReasons.Length > 0)
            warnings.Add($"{scope} top {edgeKind} reasons: {string.Join(", ", topReasons)}");
    }

    private static string? ReadJsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadJsonInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool IsRelationshipIndexRun(CodeNode node) =>
        (node.Type == CodeNodeType.IndexRun
        || (node.Type == CodeNodeType.Diagnostic
            && node.Properties.TryGetValue("externalKind", out var externalKind)
            && string.Equals(externalKind, "IndexRun", StringComparison.Ordinal)))
        && (!node.Properties.TryGetValue("indexRunKind", out var indexRunKind)
            || !string.Equals(indexRunKind, "IndexScopeCatalog", StringComparison.Ordinal));

    private static HashSet<string>? ReadActiveTypeScriptScopes(IEnumerable<CodeNode> nodes)
    {
        var catalog = nodes
            .Where(node => node.Type == CodeNodeType.Diagnostic
                && node.Properties.TryGetValue("externalKind", out var externalKind)
                && string.Equals(externalKind, "IndexRun", StringComparison.Ordinal)
                && node.Properties.TryGetValue("indexRunKind", out var indexRunKind)
                && string.Equals(indexRunKind, "IndexScopeCatalog", StringComparison.Ordinal)
                && node.Properties.TryGetValue("language", out var language)
                && string.Equals(language, "TypeScript", StringComparison.OrdinalIgnoreCase))
            .MaxBy(node => node.LastIndexedAt ?? node.UpdatedAt ?? node.CreatedAt);
        if (catalog is null
            || !catalog.Properties.TryGetValue("resolutionScopes", out var scopesJson)
            || string.IsNullOrWhiteSpace(scopesJson))
        {
            return null;
        }

        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
            return scopes
                .Select(NormalizeResolutionScope)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeResolutionScope(string scope) =>
        scope.Replace('\\', '/').TrimEnd('/');

    private sealed record RelationshipTrust(
        string Confidence,
        string Reason,
        DateTimeOffset? LastFullIndex,
        DateTimeOffset? LastIncrementalIndex,
        int ExternalOrUnindexedCount,
        int UnresolvedLocalCount,
        int IndeterminateCount,
        int DuplicateCount,
        int SyntheticCount,
        IReadOnlyList<string> Samples,
        IReadOnlyDictionary<string, long> EvidenceCounts);

    private sealed record ParsedIndexRun(
        string ProjectContext,
        string Mode,
        string Language,
        string ResolutionScope,
        int SchemaVersion,
        bool UsedFullResolutionCatalog,
        int ScannedFiles,
        int IngestedFiles,
        int ResolvedRelationships,
        int AttemptedCalls,
        int AttemptedReferences,
        int ExternalOrUnindexedCount,
        int UnresolvedLocalCount,
        int IndeterminateCount,
        int DuplicateCount,
        int SyntheticCount,
        int LegacyUnresolvedEstimate,
        IReadOnlyDictionary<string, int> CallReasons,
        IReadOnlyDictionary<string, int> ReferenceReasons,
        IReadOnlyDictionary<string, int> EdgeEvidenceCounts,
        IReadOnlyList<string> Samples,
        DateTimeOffset? Timestamp);
}
