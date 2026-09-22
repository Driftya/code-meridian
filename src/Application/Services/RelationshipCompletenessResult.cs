using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record RelationshipCompletenessResult(
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
    IReadOnlyDictionary<string, long>? EvidenceCounts = null)
{
    internal void AppendWarning(StringBuilder builder)
    {
        if (Confidence == "High")
            return;

        builder.AppendLine($"> Relationship completeness: **{Confidence}** — {Reason}. Empty relationship results are not proof that a change is safe.");
        builder.AppendLine();
    }

    internal string WarningSuffix =>
        Confidence == "High"
            ? string.Empty
            : $" Relationship completeness is {Confidence.ToLowerInvariant()}: {Reason}. An empty relationship result is not proof that a change is safe.";

    internal void AppendEvidence(StringBuilder builder)
    {
        if (Confidence == "Unknown" && EvidenceCounts is not { Count: > 0 })
            return;

        if (Confidence != "Unknown")
            builder.AppendLine(
                $"**Relationship outcomes:** {UnresolvedLocalCount} unresolved local, "
                + $"{IndeterminateCount} indeterminate, {ExternalOrUnindexedCount} external/unindexed, "
                + $"{DuplicateCount} duplicate candidate(s), {SyntheticCount} synthetic edge(s)");
        if (Samples.Count > 0)
            builder.AppendLine($"**Relationship failure samples:** {string.Join("; ", Samples)}");
        if (EvidenceCounts is { Count: > 0 })
            builder.AppendLine($"**Persisted edge evidence:** {string.Join(", ", EvidenceCounts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))}");
    }
}
