namespace CodeMeridian.Core.Projects;

public sealed record ProjectProfile(
    string Project,
    DateTimeOffset ObservedAt,
    string DiscoveryState,
    int FileCount,
    IReadOnlyList<ProjectFileKindSummary> FileKinds,
    IReadOnlyList<ProjectAnalysisRoot> AnalysisRoots,
    int AnalysisRootCount,
    IReadOnlyList<ProjectAnalyzerProfile> Analyzers,
    IReadOnlyList<string> Warnings,
    int WarningCount)
{
    public string ContractVersion { get; init; } = "1.0";
    public string EvidenceSource { get; init; } = "local_discovery";
}
