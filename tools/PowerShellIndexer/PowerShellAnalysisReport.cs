namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellAnalysisReport(string Project, string Status, PowerShellWorkerIdentity Worker,
    int RequestedFiles, IReadOnlyList<PowerShellFileAnalysis> Files)
{
    public string ContractVersion { get; init; } = "1.0";
    // Parser execution and conservative static bindings do not certify generic query coverage.
    public string Coverage { get; init; } = "unknown";
}
