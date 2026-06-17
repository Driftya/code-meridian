namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed record SessionEvaluationOptions(
    DirectoryInfo Root,
    string? Project,
    FileSystemInfo? SessionPath,
    string GitBase);

internal sealed record SessionChangeSet(
    IReadOnlySet<string> ChangedFiles);

internal sealed record SessionUsefulnessReport(
    string Rating,
    FileInfo SessionFile,
    IReadOnlySet<string> SuggestedFiles,
    IReadOnlySet<string> SuggestedTests,
    IReadOnlySet<string> ChangedFiles,
    IReadOnlySet<string> ChangedTests,
    IReadOnlySet<string> RunTests,
    int GraphCalls,
    int ExactTargets,
    int FileOnlyTargets,
    int HeuristicTargets,
    int StaleTargets,
    int StaleWarnings,
    int ManualFallbackCommands,
    IReadOnlyList<string> Notes)
{
    public int SuggestedFilesEdited => SuggestedFiles.Count(ChangedFiles.Contains);

    public int SuggestedTestsChangedOrRun => SuggestedTests.Count(test => ChangedTests.Contains(test) || RunTests.Contains(test));
}
