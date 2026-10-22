namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintRuleOutcome(
    string RuleId,
    string Family,
    string Severity,
    string Status,
    string Coverage,
    string Reason,
    IReadOnlyList<string> RequiredCapabilities,
    int EvaluatedFiles,
    int FindingCount,
    bool FindingsTruncated,
    IReadOnlyList<RepositoryConstraintFinding> Findings);
