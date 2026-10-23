namespace CodeMeridian.Sdk;

public sealed record RepositoryConstraintEvaluationResponse(
    long Generation, string Status, string Coverage, string? TargetStatus,
    IReadOnlyList<RepositoryConstraintRuleResponse> Rules, IReadOnlyList<RepositoryConstraintDiagnosticResponse> Diagnostics)
{
    public string ContractVersion { get; init; } = "1.0";
}
