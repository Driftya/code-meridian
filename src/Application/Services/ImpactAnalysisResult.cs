using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record ImpactAnalysisResult(
    string ContractVersion,
    string TargetId,
    int MaxDepth,
    bool ImpactFound,
    bool ClassLikeTarget,
    string OverallConfidence,
    RelationshipCompletenessResult RelationshipCompleteness,
    IReadOnlyList<ImpactFindingResult> Findings,
    bool Truncated)
{
    public string ToMarkdown(ContextDetailLevel detailLevel, bool includeConfidence)
    {
        if (!ImpactFound)
        {
            return $"No callers found for `{TargetId}` within {MaxDepth} hops. "
                   + "The node may not exist in the graph or has no inbound dependencies."
                   + RelationshipCompleteness.WarningSuffix;
        }

        if (detailLevel == ContextDetailLevel.Summary)
        {
            if (!includeConfidence)
            {
                return $"Impact summary for `{TargetId}`: {Findings.Count} affected graph elements within {MaxDepth} hops. "
                       + $"Nearest distance: {Findings.Min(finding => finding.Distance)}. "
                       + $"Farthest distance: {Findings.Max(finding => finding.Distance)}.";
            }

            return $"Impact summary for `{TargetId}`: {Findings.Count} affected code elements within {MaxDepth} hops. "
                   + $"Confidence: {OverallConfidence}. "
                   + $"{Count("Proven")} proven, {Count("Heuristic")} heuristic, {Count("Unknown")} unknown risk.";
        }

        if (includeConfidence)
            return ToConfidenceMarkdown();

        if (ClassLikeTarget && Findings.Any(finding => finding.EvidenceBucket != "direct-class"))
            return ToClassLikeMarkdown(detailLevel);

        var builder = new StringBuilder();
        RelationshipCompleteness.AppendWarning(builder);
        builder.AppendLine($"## Impact Analysis — `{TargetId}`");
        builder.AppendLine($"**{Findings.Count}** code elements would be affected by changing this (up to {MaxDepth} hops):\n");
        builder.AppendLine("| Distance | Type | Name | File |");
        builder.AppendLine("|----------|------|------|------|");

        foreach (var finding in Findings)
        {
            var file = finding.Node.FilePath is not null ? $"`{finding.Node.FilePath}`" : "—";
            builder.AppendLine($"| {finding.Distance} | {finding.Node.Type} | `{finding.Node.Name}` | {file} |");
        }

        return builder.ToString();
    }

    private string ToConfidenceMarkdown()
    {
        var builder = new StringBuilder();
        RelationshipCompleteness.AppendWarning(builder);
        builder.AppendLine($"## Impact Analysis — `{TargetId}`");
        builder.AppendLine($"**{Findings.Count}** code elements would be affected by changing this (up to {MaxDepth} hops):");
        builder.AppendLine($"**Impact confidence:** {OverallConfidence}");
        builder.AppendLine($"**Trust summary:** {Count("Proven")} proven callers, {Count("Heuristic")} heuristic callers, {Count("Unknown")} unknown-risk nodes");
        builder.AppendLine();

        AppendConfidenceSection(builder, "Proven callers", "Proven");
        AppendConfidenceSection(builder, "Heuristic callers", "Heuristic");
        AppendConfidenceSection(builder, "Unknown risk", "Unknown");
        builder.AppendLine("> Proven callers use structural graph paths without stale metadata or low-confidence edges. "
                           + "Heuristic callers cross abstraction edges, route-like nodes, or inferred edges. "
                           + "Unknown risk means stale metadata lowers trust and exact blast radius may require re-indexing.");
        return builder.ToString();
    }

    private string ToClassLikeMarkdown(ContextDetailLevel detailLevel)
    {
        var buckets = new[]
        {
            (Key: "direct-class", Title: "Direct class callers", Reason: "direct class-level usage edge"),
            (Key: "member", Title: "Member callers", Reason: "caller reaches a contained member on the path to this type"),
            (Key: "dependency", Title: "Dependency/composition callers", Reason: "dependency/composition or abstraction edge near the target type"),
            (Key: "workflow", Title: "Workflow-adjacent callers", Reason: "workflow or metadata-adjacent caller evidence")
        };
        var builder = new StringBuilder();
        builder.AppendLine($"## Impact Analysis — `{TargetId}`");
        builder.AppendLine($"**{Findings.Count}** code elements would be affected by changing this class or interface target (up to {MaxDepth} hops):");
        builder.AppendLine(
            $"**Caller evidence:** {CountBucket("direct-class")} direct class callers, {CountBucket("member")} member callers, "
            + $"{CountBucket("dependency")} dependency/composition callers, {CountBucket("workflow")} workflow-adjacent callers");
        builder.AppendLine();

        foreach (var bucket in buckets)
        {
            var findings = Findings.Where(finding => finding.EvidenceBucket == bucket.Key).ToArray();
            builder.AppendLine($"### {bucket.Title} ({findings.Length})");
            if (findings.Length == 0)
            {
                builder.AppendLine("- none");
            }
            else
            {
                foreach (var finding in findings.Take(detailLevel == ContextDetailLevel.Full ? 50 : 10))
                    builder.AppendLine($"- d{finding.Distance}: **{finding.Node.Type}** `{finding.Node.Name}`{finding.Node.FormatLocation()} — {bucket.Reason}");
                if (detailLevel != ContextDetailLevel.Full && findings.Length > 10)
                    builder.AppendLine($"- ...{findings.Length - 10} more");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private void AppendConfidenceSection(StringBuilder builder, string title, string classification)
    {
        var findings = Findings
            .Where(finding => finding.Classification == classification)
            .OrderBy(finding => finding.Distance)
            .ThenBy(finding => finding.Node.Type, StringComparer.Ordinal)
            .ThenBy(finding => finding.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        builder.AppendLine($"### {title} ({findings.Length})");
        if (findings.Length == 0)
        {
            builder.AppendLine("- None");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Distance | Type | Name | File | Why | Path |");
        builder.AppendLine("|---:|---|---|---|---|---|");
        foreach (var finding in findings)
        {
            var file = finding.Node.FilePath is not null ? $"`{finding.Node.FilePath}`" : "—";
            var reason = string.IsNullOrWhiteSpace(finding.Reason) ? "—" : finding.Reason;
            builder.AppendLine($"| {finding.Distance} | {finding.Node.Type} | `{finding.Node.Name}` | {file} | {EscapeTableCell(reason)} | {EscapeTableCell(finding.Path)} |");
        }
        builder.AppendLine();
    }

    private int Count(string classification) =>
        Findings.Count(finding => finding.Classification == classification);

    private int CountBucket(string bucket) =>
        Findings.Count(finding => finding.EvidenceBucket == bucket);

    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
