namespace CodeMeridian.Sdk;

public sealed record RepositoryConstraintResponse(
    string Project, string? TargetPath, long? Generation, long? LatestGeneration,
    string Status, string Coverage, string? PolicyPath, string? PolicyFingerprint,
    RepositoryConstraintEvaluationResponse? Evaluation, IReadOnlyList<RepositoryConstraintDiagnosticResponse> Diagnostics)
{
    public string ContractVersion { get; init; } = "1.0";
}
