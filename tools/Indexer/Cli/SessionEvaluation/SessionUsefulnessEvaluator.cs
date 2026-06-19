namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class SessionUsefulnessEvaluator(
    SessionEvidenceReader evidenceReader,
    ISessionChangeSource changeSource)
{
    public async Task<SessionUsefulnessReport> EvaluateAsync(SessionEvaluationOptions options, CancellationToken cancellationToken = default)
    {
        var sessionFile = ResolveSessionFile(options.Root, options.SessionPath);
        var events = evidenceReader.Read(sessionFile);
        var projectEvents = FilterByProject(events, options.Project);
        var changes = await changeSource.GetChangesAsync(options.Root, options.GitBase, cancellationToken);

        var suggestedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestedTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        var graphCalls = 0;
        var exactTargets = 0;
        var fileOnlyTargets = 0;
        var heuristicTargets = 0;
        var staleTargets = 0;
        var staleWarnings = 0;
        var manualFallbackCommands = 0;
        var contextPackFullSuccesses = 0;
        var contextPackDegradedSuccesses = 0;
        var contextPackHardFailures = 0;
        var toolStats = new Dictionary<string, ToolPrecisionBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in projectEvents)
        {
            var kind = item.Kind ?? string.Empty;
            var toolName = item.ToolName ?? string.Empty;
            var normalizedFiles = item.Files.Select(SessionPathNormalizer.Normalize).Where(path => path.Length > 0).ToArray();
            var normalizedTests = item.Tests.Select(SessionPathNormalizer.Normalize).Where(path => path.Length > 0).ToArray();
            var toolStat = GetToolStat(toolStats, toolName);

            if (IsGraphCall(kind, toolName))
            {
                graphCalls++;
                if (toolStat is not null)
                    toolStat.GraphCalls++;
            }

            if (IsManualFallback(kind, item.Command))
            {
                manualFallbackCommands++;
                foreach (var stat in toolStats.Values)
                    stat.ManualFallbackCommands++;
            }

            if (item.StaleWarning == true || kind.Equals("stale-warning", StringComparison.OrdinalIgnoreCase))
            {
                staleWarnings++;
                foreach (var stat in toolStats.Values)
                    stat.StaleWarnings++;
            }

            CountContextPackStatus(
                toolName,
                item.ContextPackStatus,
                ref contextPackFullSuccesses,
                ref contextPackDegradedSuccesses,
                ref contextPackHardFailures);

            CountTargetConfidence(
                item.TargetConfidence,
                ref exactTargets,
                ref fileOnlyTargets,
                ref heuristicTargets,
                ref staleTargets,
                toolStat);

            foreach (var file in normalizedFiles)
            {
                if (IsSuggestionEvent(kind, toolName))
                {
                    suggestedFiles.Add(file);
                    toolStat?.SuggestedFiles.Add(file);
                }
            }

            foreach (var test in normalizedTests)
            {
                if (IsSuggestionEvent(kind, toolName))
                {
                    suggestedTests.Add(test);
                    toolStat?.SuggestedTests.Add(test);
                }

                if (IsTestRunEvent(kind, item.Command))
                {
                    runTests.Add(test);
                    toolStat?.RunTests.Add(test);
                }
            }
        }

        foreach (var changedTest in changes.ChangedFiles.Where(SessionPathNormalizer.IsLikelyTestPath))
            runTests.Remove(changedTest);

        var changedTests = changes.ChangedFiles
            .Where(SessionPathNormalizer.IsLikelyTestPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (projectEvents.Count == 0)
            notes.Add("No session events matched the requested project.");

        if (suggestedFiles.Count == 0)
            notes.Add("No suggested files were recorded; agents should log CodeMeridian tool results or imported transcript facts.");

        if (changes.ChangedFiles.Count == 0)
            notes.Add("No changed files were detected from git diff.");

        var rating = Rate(
            suggestedFiles.Count,
            suggestedFiles.Count(changes.ChangedFiles.Contains),
            suggestedTests.Count,
            suggestedTests.Count(test => changedTests.Contains(test) || runTests.Contains(test)),
            graphCalls,
            manualFallbackCommands,
            staleWarnings);
        var precisionFeedback = BuildPrecisionFeedback(
            options.Project,
            sessionFile,
            changedTests,
            changes.ChangedFiles,
            toolStats);

        return new SessionUsefulnessReport(
            rating,
            sessionFile,
            suggestedFiles,
            suggestedTests,
            changes.ChangedFiles,
            changedTests,
            runTests,
            graphCalls,
            exactTargets,
            fileOnlyTargets,
            heuristicTargets,
            staleTargets,
            staleWarnings,
            manualFallbackCommands,
            contextPackFullSuccesses,
            contextPackDegradedSuccesses,
            contextPackHardFailures,
            precisionFeedback,
            notes);
    }

    private static FileInfo ResolveSessionFile(DirectoryInfo root, FileSystemInfo? sessionPath)
    {
        if (sessionPath is FileInfo explicitFile)
            return explicitFile;

        if (sessionPath is DirectoryInfo explicitDirectory)
            return LatestSessionFile(explicitDirectory);

        var defaultDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".meridian", "sessions"));
        return LatestSessionFile(defaultDirectory);
    }

    private static FileInfo LatestSessionFile(DirectoryInfo directory)
    {
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"Session evidence directory not found: {directory.FullName}");

        var file = directory
            .EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
            .FirstOrDefault();

        return file ?? throw new FileNotFoundException($"No session evidence .jsonl files found in {directory.FullName}");
    }

    private static IReadOnlyList<SessionEvidenceEvent> FilterByProject(
        IReadOnlyList<SessionEvidenceEvent> events,
        string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
            return events;

        return events
            .Where(item => string.IsNullOrWhiteSpace(item.Project)
                || string.Equals(item.Project, project, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsGraphCall(string kind, string toolName) =>
        kind.Equals("graph-call", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("codemeridian-tool", StringComparison.OrdinalIgnoreCase)
        || toolName.StartsWith("mcp__CodeMeridian.", StringComparison.OrdinalIgnoreCase)
        || toolName.StartsWith("CodeMeridian.", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuggestionEvent(string kind, string toolName) =>
        IsGraphCall(kind, toolName)
        || kind.Equals("suggestion", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("tool-result", StringComparison.OrdinalIgnoreCase);

    private static bool IsManualFallback(string kind, string? command)
    {
        if (kind.Equals("manual-fallback", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!kind.Equals("command", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(command))
            return false;

        return command.StartsWith("rg ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("grep ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("find ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("Get-ChildItem", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("Select-String", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestRunEvent(string kind, string? command)
    {
        if (kind.Equals("test-run", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!kind.Equals("command", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(command))
            return false;

        return command.Contains("dotnet test", StringComparison.OrdinalIgnoreCase)
            || command.Contains("npm test", StringComparison.OrdinalIgnoreCase)
            || command.Contains("pnpm test", StringComparison.OrdinalIgnoreCase)
            || command.Contains("yarn test", StringComparison.OrdinalIgnoreCase)
            || command.Contains("vitest", StringComparison.OrdinalIgnoreCase)
            || command.Contains("pytest", StringComparison.OrdinalIgnoreCase);
    }

    private static void CountTargetConfidence(
        string? targetConfidence,
        ref int exactTargets,
        ref int fileOnlyTargets,
        ref int heuristicTargets,
        ref int staleTargets,
        ToolPrecisionBuilder? toolStat)
    {
        if (string.IsNullOrWhiteSpace(targetConfidence))
            return;

        foreach (var value in targetConfidence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (value.ToLowerInvariant())
            {
                case "exact":
                    exactTargets++;
                    if (toolStat is not null)
                        toolStat.ExactTargets++;
                    break;
                case "file-only":
                case "file":
                    fileOnlyTargets++;
                    if (toolStat is not null)
                        toolStat.FileOnlyTargets++;
                    break;
                case "heuristic":
                    heuristicTargets++;
                    if (toolStat is not null)
                        toolStat.HeuristicTargets++;
                    break;
                case "stale":
                    staleTargets++;
                    if (toolStat is not null)
                        toolStat.StaleTargets++;
                    break;
            }
        }
    }

    private static void CountContextPackStatus(
        string toolName,
        string? contextPackStatus,
        ref int fullSuccesses,
        ref int degradedSuccesses,
        ref int hardFailures)
    {
        if (!IsBuildMinimalContextTool(toolName) || string.IsNullOrWhiteSpace(contextPackStatus))
            return;

        switch (contextPackStatus.Trim().ToLowerInvariant())
        {
            case "full":
            case "success":
                fullSuccesses++;
                break;
            case "degraded":
            case "partial":
                degradedSuccesses++;
                break;
            case "failed":
            case "hard-failure":
            case "hard_failure":
                hardFailures++;
                break;
        }
    }

    private static bool IsBuildMinimalContextTool(string toolName) =>
        toolName.EndsWith("build_minimal_context", StringComparison.OrdinalIgnoreCase);

    private static string Rate(
        int suggestedFiles,
        int suggestedFilesEdited,
        int suggestedTests,
        int suggestedTestsChangedOrRun,
        int graphCalls,
        int manualFallbackCommands,
        int staleWarnings)
    {
        if (graphCalls == 0 || suggestedFiles == 0)
            return "unknown";

        var fileRatio = suggestedFilesEdited / (double)suggestedFiles;
        var testRatio = suggestedTests == 0 ? 0.0 : suggestedTestsChangedOrRun / (double)suggestedTests;
        var fallbackPressure = manualFallbackCommands > graphCalls * 3;

        if (fileRatio >= 0.67 && (suggestedTests == 0 || testRatio >= 0.5) && !fallbackPressure && staleWarnings == 0)
            return "high";

        if (fileRatio > 0 || testRatio > 0 || graphCalls > manualFallbackCommands)
            return "partial";

        return "low";
    }

    private static ToolPrecisionBuilder? GetToolStat(
        IDictionary<string, ToolPrecisionBuilder> toolStats,
        string toolName)
    {
        if (!IsGraphToolForFeedback(toolName))
            return null;

        if (!toolStats.TryGetValue(toolName, out var stat))
        {
            stat = new ToolPrecisionBuilder(toolName);
            toolStats[toolName] = stat;
        }

        return stat;
    }

    private static bool IsGraphToolForFeedback(string toolName) =>
        !string.IsNullOrWhiteSpace(toolName)
        && (toolName.EndsWith("find_implementation_surface", StringComparison.OrdinalIgnoreCase)
            || toolName.EndsWith("analyze_feature_implementation_path", StringComparison.OrdinalIgnoreCase));

    private static SessionPrecisionFeedback BuildPrecisionFeedback(
        string? project,
        FileInfo sessionFile,
        IReadOnlySet<string> changedTests,
        IReadOnlySet<string> changedFiles,
        IReadOnlyDictionary<string, ToolPrecisionBuilder> toolStats)
    {
        var tools = toolStats.Values
            .OrderBy(builder => builder.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(builder => builder.Build(changedFiles, changedTests))
            .ToArray();

        return new SessionPrecisionFeedback(
            project,
            sessionFile.FullName,
            DateTimeOffset.UtcNow,
            tools);
    }

    private sealed class ToolPrecisionBuilder(string toolName)
    {
        public string ToolName { get; } = toolName;
        public HashSet<string> SuggestedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SuggestedTests { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RunTests { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int GraphCalls { get; set; }
        public int ExactTargets { get; set; }
        public int FileOnlyTargets { get; set; }
        public int HeuristicTargets { get; set; }
        public int StaleTargets { get; set; }
        public int StaleWarnings { get; set; }
        public int ManualFallbackCommands { get; set; }

        public ToolPrecisionFeedback Build(IReadOnlySet<string> changedFiles, IReadOnlySet<string> changedTests)
        {
            var fileSignals = SuggestedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new PathPrecisionFeedback(
                    path,
                    1,
                    changedFiles.Contains(path) ? 1 : 0,
                    changedFiles.Contains(path) ? 0 : 1))
                .ToArray();
            var testSignals = SuggestedTests
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new PathPrecisionFeedback(
                    path,
                    1,
                    changedTests.Contains(path) || RunTests.Contains(path) ? 1 : 0,
                    changedTests.Contains(path) || RunTests.Contains(path) ? 0 : 1))
                .ToArray();

            return new ToolPrecisionFeedback(
                ToolName,
                fileSignals.Length,
                fileSignals.Sum(signal => signal.AcceptedCount),
                fileSignals.Sum(signal => signal.IgnoredCount),
                testSignals.Length,
                testSignals.Sum(signal => signal.AcceptedCount),
                testSignals.Sum(signal => signal.IgnoredCount),
                ExactTargets,
                FileOnlyTargets,
                HeuristicTargets,
                StaleTargets,
                StaleWarnings,
                ManualFallbackCommands,
                fileSignals,
                testSignals);
        }
    }
}
