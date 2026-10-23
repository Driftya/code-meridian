namespace CodeMeridian.Sdk;

public sealed record RepositoryConstraintRuleResponse(
    string RuleId, string Family, string Severity, string Status, string Coverage, string Reason,
    IReadOnlyList<string> RequiredCapabilities, int EvaluatedFiles, int FindingCount, bool FindingsTruncated,
    IReadOnlyList<RepositoryConstraintFindingResponse> Findings);
