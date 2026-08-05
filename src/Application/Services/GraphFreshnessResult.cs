using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record GraphFreshnessResult(
    string ContractVersion,
    string? ProjectContext,
    string? Query,
    bool NodesFound,
    int HighConfidenceCount,
    int MediumConfidenceCount,
    int LowConfidenceCount,
    RelationshipCompletenessResult? RelationshipCompleteness,
    IReadOnlyList<GraphFreshnessFindingResult> Findings,
    bool FindingsTruncated,
    string? ProjectHint)
{
    public string ToMarkdown()
    {
        if (!NodesFound)
        {
            return $"No graph nodes found{(ProjectContext is not null ? $" in '{ProjectContext}'" : string.Empty)}"
                   + $"{(Query is not null ? $" for `{Query}`" : string.Empty)}.{ProjectHint}";
        }

        var relationship = RelationshipCompleteness!;
        var builder = new StringBuilder();
        builder.AppendLine($"## Graph Freshness{(ProjectContext is not null ? $" - {ProjectContext}" : string.Empty)}");
        if (!string.IsNullOrWhiteSpace(Query))
            builder.AppendLine($"**Query:** `{Query}`");
        builder.AppendLine($"**Trust summary (node metadata):** {HighConfidenceCount} High, {MediumConfidenceCount} Medium, {LowConfidenceCount} Low confidence");
        builder.AppendLine($"**Relationship completeness:** {relationship.Confidence} — {relationship.Reason}");
        relationship.AppendEvidence(builder);
        builder.AppendLine($"**Last full index:** {relationship.LastFullIndex?.ToString("u") ?? "unknown"}");
        builder.AppendLine($"**Last incremental index:** {relationship.LastIncrementalIndex?.ToString("u") ?? "none recorded"}\n");
        builder.AppendLine("| Confidence | Node | Source verification | Line metadata | Last indexed / content updated | Reason |");
        builder.AppendLine("|---|---|---|---|---|---|");

        foreach (var finding in Findings)
        {
            var indexed = finding.LastIndexedAt?.ToString("u") ?? "unknown";
            var updated = finding.UpdatedAt?.ToString("u") ?? "unknown";
            builder.AppendLine($"| {finding.Confidence} | `{finding.Node.Name}` ({finding.Node.Type}) | {finding.SourceVerification} | {finding.LineMetadata} | indexed {indexed}<br>updated {updated} | {finding.Reason} |");
        }

        if (FindingsTruncated)
            builder.AppendLine($"\n> Findings were truncated to {Findings.Count} bounded samples.");

        return builder.ToString();
    }
}
