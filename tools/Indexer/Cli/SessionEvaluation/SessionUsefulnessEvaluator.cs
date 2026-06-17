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

        foreach (var item in projectEvents)
        {
            var kind = item.Kind ?? string.Empty;
            var toolName = item.ToolName ?? string.Empty;

            if (IsGraphCall(kind, toolName))
                graphCalls++;

            if (IsManualFallback(kind, item.Command))
                manualFallbackCommands++;

            if (item.StaleWarning == true || kind.Equals("stale-warning", StringComparison.OrdinalIgnoreCase))
                staleWarnings++;

            CountTargetConfidence(item.TargetConfidence, ref exactTargets, ref fileOnlyTargets, ref heuristicTargets, ref staleTargets);

            foreach (var file in item.Files.Select(SessionPathNormalizer.Normalize).Where(path => path.Length > 0))
            {
                if (IsSuggestionEvent(kind, toolName))
                    suggestedFiles.Add(file);
            }

            foreach (var test in item.Tests.Select(SessionPathNormalizer.Normalize).Where(path => path.Length > 0))
            {
                if (IsSuggestionEvent(kind, toolName))
                    suggestedTests.Add(test);

                if (IsTestRunEvent(kind, item.Command))
                    runTests.Add(test);
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
        ref int staleTargets)
    {
        if (string.IsNullOrWhiteSpace(targetConfidence))
            return;

        foreach (var value in targetConfidence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (value.ToLowerInvariant())
            {
                case "exact":
                    exactTargets++;
                    break;
                case "file-only":
                case "file":
                    fileOnlyTargets++;
                    break;
                case "heuristic":
                    heuristicTargets++;
                    break;
                case "stale":
                    staleTargets++;
                    break;
            }
        }
    }

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
}
