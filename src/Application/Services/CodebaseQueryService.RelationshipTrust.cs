using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    private async Task<RelationshipTrust> GetRelationshipTrustAsync(
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var nativeRuns = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                TypeFilter = CodeNodeType.IndexRun,
                ProjectContext = projectContext,
                Limit = 100
            },
            cancellationToken);
        var compatibleRuns = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                TypeFilter = CodeNodeType.Diagnostic,
                NameFilter = "C# index run",
                ProjectContext = projectContext,
                Limit = 100
            },
            cancellationToken);
        var indexRuns = nativeRuns
            .Concat(compatibleRuns)
            .Where(IsRelationshipIndexRun)
            .DistinctBy(node => node.Id)
            .OrderByDescending(node => node.LastIndexedAt ?? node.UpdatedAt ?? node.CreatedAt)
            .ToArray();
        if (indexRuns.Length == 0)
        {
            return new RelationshipTrust(
                "Unknown",
                "no index-run relationship statistics are available",
                null,
                null);
        }

        var parsedRuns = indexRuns.Select(ParseIndexRun).ToArray();
        var latest = parsedRuns[0];
        var latestFull = parsedRuns.FirstOrDefault(run => run.Mode == "full");
        var latestIncremental = parsedRuns.FirstOrDefault(run => run.Mode == "incremental");
        var warnings = new List<string>();
        var confidence = "High";

        if (!latest.UsedFullResolutionCatalog)
        {
            confidence = "Low";
            warnings.Add("the latest incremental pass did not use a full relationship-resolution catalog");
        }

        if (latest.UnresolvedCount > 0)
        {
            if (confidence == "High")
                confidence = "Medium";
            warnings.Add($"the latest pass left {latest.UnresolvedCount} attempted local relationship(s) unresolved");
        }

        if (latest.Mode == "incremental" && latestFull is not null &&
            latest.ScannedFiles > 0 && latest.IngestedFiles * 10 <= latest.ScannedFiles &&
            latest.ResolvedRelationships * 2 < latestFull.ResolvedRelationships)
        {
            confidence = "Low";
            warnings.Add("resolved relationships dropped by more than 50% after a small incremental batch");
        }

        var reason = warnings.Count == 0
            ? "the latest index pass used a full resolution catalog and reported no unresolved local relationships"
            : string.Join("; ", warnings);
        return new RelationshipTrust(
            confidence,
            reason,
            latestFull?.Timestamp,
            latestIncremental?.Timestamp);
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

    private static ParsedIndexRun ParseIndexRun(CodeNode node)
    {
        var properties = node.Properties;
        var mode = Read("mode") ?? (node.Name.StartsWith("incremental", StringComparison.OrdinalIgnoreCase) ? "incremental" : "full");
        var attempted = ReadInt("attemptedCallEdges") + ReadInt("attemptedReferenceEdges");
        var resolved = ReadInt("resolvedCallEdges") + ReadInt("resolvedReferenceEdges");
        return new ParsedIndexRun(
            mode,
            ReadBool("usedFullResolutionCatalog"),
            ReadInt("scannedFileCount"),
            ReadInt("ingestedFileCount"),
            resolved,
            Math.Max(0, attempted - resolved),
            node.LastIndexedAt ?? node.UpdatedAt ?? node.CreatedAt);

        string? Read(string key) => properties.TryGetValue(key, out var value) ? value : null;
        int ReadInt(string key) => int.TryParse(Read(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        bool ReadBool(string key) => bool.TryParse(Read(key), out var value) && value;
    }

    private static bool IsRelationshipIndexRun(CodeNode node) =>
        node.Type == CodeNodeType.IndexRun
        || (node.Type == CodeNodeType.Diagnostic
            && node.Properties.TryGetValue("externalKind", out var externalKind)
            && string.Equals(externalKind, "IndexRun", StringComparison.Ordinal));

    private sealed record RelationshipTrust(
        string Confidence,
        string Reason,
        DateTimeOffset? LastFullIndex,
        DateTimeOffset? LastIncrementalIndex);

    private sealed record ParsedIndexRun(
        string Mode,
        bool UsedFullResolutionCatalog,
        int ScannedFiles,
        int IngestedFiles,
        int ResolvedRelationships,
        int UnresolvedCount,
        DateTimeOffset? Timestamp);
}
