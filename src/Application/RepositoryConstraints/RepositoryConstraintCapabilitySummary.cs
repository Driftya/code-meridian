namespace CodeMeridian.Application.RepositoryConstraints;

public sealed record RepositoryConstraintCapabilitySummary(
    bool CanEvaluate,
    string Status,
    string Coverage,
    long? Generation,
    IReadOnlyList<string> RuleFamilies,
    IReadOnlyList<string> Reasons)
{
    public string ContractVersion { get; init; } = "1.0";
    public string? EvidenceFingerprint { get; init; }

    public static RepositoryConstraintCapabilitySummary From(RepositoryConstraintQueryResult result, long generation) => new(
        result.Generation == generation && result.Evaluation is not null && result.Status is "evaluated" or "incomplete",
        result.Generation == generation ? result.Status : "stale",
        result.Generation == generation ? result.Coverage : "unknown", result.Generation,
        result.Evaluation?.Rules.Select(rule => rule.Family).Distinct().ToArray() ?? [],
        result.Generation != generation ? ["Constraint evidence changed while reading the profile."]
            : result.Diagnostics.Concat(result.Evaluation?.Diagnostics ?? []).Select(diagnostic => diagnostic.Reason).Distinct().Take(8).ToArray())
        { EvidenceFingerprint = result.ConstraintEvidenceFingerprint };
}
