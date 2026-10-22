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
    public string ContractVersion => "1.0";
    public string ProjectModel => "synthetic_compilation";
    public string Coverage => FailedFiles > 0 || SkippedFiles > 0 || ParseErrors > 0 ? "partial" : "unknown";
    public int ExitCode => FailedFiles > 0 || ParseErrors > 0 ? 2 : 0;
    public IReadOnlyList<string> Limitations =>
    [
        $"C# worker: {RequestedFiles} requested, {ReadFiles} read, {ExtractedFiles} extracted, {FailedFiles} failed, {SkippedFiles} skipped; {ParseErrors} parse errors.",
        "Project loading and callable definition/dependency coverage remain unaudited; the synthetic compilation does not establish project membership or complete semantic coverage."
    ];
}
