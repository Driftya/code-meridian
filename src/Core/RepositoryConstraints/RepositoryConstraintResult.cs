namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintResult(
    long Generation,
    string Status,
    string Coverage,
    string? TargetStatus,
    IReadOnlyList<RepositoryConstraintRuleOutcome> Rules,
    IReadOnlyList<RepositoryConstraintDiagnostic> Diagnostics)
{
    public string ContractVersion { get; init; } = "1.0";
}
