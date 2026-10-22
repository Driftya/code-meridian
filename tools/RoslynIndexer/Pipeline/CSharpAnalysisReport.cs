namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>Observed worker execution evidence; successful extraction is not an audit of semantic coverage.</summary>
public sealed record CSharpAnalysisReport(
    int RequestedFiles,
    int ReadFiles,
    int ExtractedFiles,
    int FailedFiles,
    int SkippedFiles,
    int ParseErrors,
    IReadOnlyList<CSharpAnalysisDiagnostic> DiagnosticSamples)
{
    public string ContractVersion => "1.2";
    public string ProjectModel => "synthetic_compilation";
    public int SemanticErrors { get; init; }
    public CSharpRelationshipSummary? CallRelationships { get; init; }
    public CSharpRelationshipSummary? TypeRelationships { get; init; }
    public CSharpCallableDefinitionSummary? CallableDefinitions { get; init; }
    public string Coverage => FailedFiles > 0 || SkippedFiles > 0 || ParseErrors > 0 || SemanticErrors > 0
        || CallRelationships?.HasGaps == true || TypeRelationships?.HasGaps == true
        || CallableDefinitions?.HasGaps == true ? "partial" : "unknown";
    public int ExitCode => FailedFiles > 0 || ParseErrors > 0 ? 2 : 0;
    public IReadOnlyList<string> Limitations =>
    [
        $"C# worker: {RequestedFiles} requested, {ReadFiles} read, {ExtractedFiles} extracted, {FailedFiles} failed, {SkippedFiles} skipped; {ParseErrors} parse errors.",
        $"The synthetic compilation reported {SemanticErrors} semantic errors; these are analysis limitations, not evaluated project-build results.",
        $"Observed relationships: calls ({Describe(CallRelationships)}); types ({Describe(TypeRelationships)}). Candidate accounting does not audit extraction completeness.",
        $"Callable syntax: {DescribeDefinitions()}. This excludes compiler-synthesized declarations and does not establish complete semantic coverage.",
        "Project loading and callable definition/dependency coverage remain unaudited; the synthetic compilation does not establish project membership or complete semantic coverage."
    ];

    private string DescribeDefinitions() => CallableDefinitions is not { } definitions ? "not reported"
        : $"{definitions.Candidates} candidates, {definitions.Represented} represented, {definitions.Unrepresented} unrepresented, {definitions.IdentityConflictDeclarations} declarations with conflicting identities";

    private static string Describe(CSharpRelationshipSummary? summary) => summary is null ? "not reported"
        : $"{summary.Attempted} attempted, {summary.ResolvedLocal} resolved local, {summary.ExternalOrUnindexed} external/unindexed, {summary.UnresolvedLocal} unresolved local, {summary.Indeterminate} indeterminate";
}
