namespace CodeMeridian.Core.Projects;

public sealed record ProjectAnalyzerOutcomeSnapshot(
    string Project,
    long Generation,
    IReadOnlyList<ProjectAnalyzerOutcome> Outcomes)
{
    public string ContractVersion { get; init; } = "1.0";

    public string? Validate()
    {
        if (ContractVersion != "1.0") return "Unsupported analyzer outcome contract version.";
        if (!ProjectProfileSnapshot.ValidName(Project, 200) || Generation <= 0)
            return "A valid project and published generation are required.";
        if (Outcomes is null || Outcomes.Count is < 1 or > 128)
            return "Between one and 128 analyzer outcomes are required.";
        var identities = new HashSet<(string, string)>();
        foreach (var outcome in Outcomes)
        {
            if (outcome is null || !ProjectProfileSnapshot.ValidName(outcome.Analyzer, 64)
                || !identities.Add((outcome.Analyzer, outcome.Scope))
                || outcome.State is not ("running" or "succeeded" or "partial" or "failed")
                || outcome.Coverage is not ("complete" or "partial" or "unknown")
                || outcome.Mode is not ("full" or "delta") || outcome.ObservedAt == default
                || !ProjectProfileSnapshot.ValidName(outcome.ReporterVersion, 200)
                || outcome.Fingerprint?.Validate() is not null
                || outcome.Limitations is null || outcome.Limitations.Count > 8
                || outcome.Limitations.Any(limit => !ProjectProfileSnapshot.ValidName(limit, 512)))
                return "Analyzer outcome metadata is invalid.";
            try
            {
                if (outcome.Scope != "." && ProjectProfilePath.Normalize(outcome.Scope) != outcome.Scope)
                    return "Outcome scopes must use normalized relative paths.";
            }
            catch (ArgumentException) { return "Outcome scope is invalid."; }
            if (outcome.Coverage == "complete" && (outcome.State != "succeeded" || outcome.Mode != "full"))
                return "Only a successful full scope can claim complete coverage.";
        }
        return null;
    }
}
