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
    int ContextPackFullSuccesses,
    int ContextPackDegradedSuccesses,
    int ContextPackHardFailures,
    SessionPrecisionFeedback PrecisionFeedback,
    IReadOnlyList<string> Notes)
{
    public int SuggestedFilesEdited => SuggestedFiles.Count(ChangedFiles.Contains);

    public int SuggestedTestsChangedOrRun => SuggestedTests.Count(test => ChangedTests.Contains(test) || RunTests.Contains(test));
}

internal sealed record SessionPrecisionFeedback(
    string? Project,
    string SessionFile,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ToolPrecisionFeedback> Tools);

internal sealed record ToolPrecisionFeedback(
    string ToolName,
    int SuggestedFileCount,
    int AcceptedFileCount,
    int IgnoredFileCount,
    int SuggestedTestCount,
    int AcceptedTestCount,
    int IgnoredTestCount,
    int ExactTargets,
    int FileOnlyTargets,
    int HeuristicTargets,
    int StaleTargets,
    int StaleWarnings,
    int ManualFallbackCommands,
    IReadOnlyList<PathPrecisionFeedback> Files,
    IReadOnlyList<PathPrecisionFeedback> Tests);

internal sealed record PathPrecisionFeedback(
    string Path,
    int SuggestedCount,
    int AcceptedCount,
    int IgnoredCount);
