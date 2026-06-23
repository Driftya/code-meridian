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
        var derivedHints = new List<DerivedMatchHint>();
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
            var hint = CreateDerivedMatchHint(item, normalizedFiles);

            if (hint is not null)
            {
                derivedHints.Add(hint);
                toolStat?.DerivedHints.Add(hint);
            }

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

            if (IsSuggestionEvent(kind, toolName))
            {
                var directSuggestionFiles = hint is null || hint.TargetFiles.Count == 0
                    ? normalizedFiles
                    : [];

                foreach (var file in directSuggestionFiles)
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
        var changedNonTestFiles = changes.ChangedFiles
            .Where(path => !SessionPathNormalizer.IsLikelyTestPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileCredits = EvaluateFileCredits(
            options.Root,
            changedNonTestFiles,
            suggestedFiles,
            derivedHints,
            changes.RenamedFromByPath);

        if (projectEvents.Count == 0)
            notes.Add("No session events matched the requested project.");

        if (suggestedFiles.Count == 0)
            notes.Add("No suggested files were recorded; agents should log CodeMeridian tool results or imported transcript facts.");

        if (changes.ChangedFiles.Count == 0)
            notes.Add("No changed files were detected from git diff.");

        if (fileCredits.DerivedSuggestedFiles.Count > 0)
        {
            notes.Add(
                $"Derived file credit applied for {fileCredits.DerivedSuggestedFiles.Count} suggested path(s) via extraction, move, rename, split, or planned slice lineage.");
        }

        var rating = Rate(
            suggestedFiles.Count,
            suggestedFiles.Count(changedNonTestFiles.Contains) + fileCredits.DerivedSuggestedFiles.Count,
            suggestedTests.Count,
            suggestedTests.Count(test => changedTests.Contains(test) || runTests.Contains(test)),
            graphCalls,
            manualFallbackCommands,
            staleWarnings);
        var precisionFeedback = BuildPrecisionFeedback(
            options.Root,
            options.Project,
            sessionFile,
            changedTests,
            changedNonTestFiles,
            changes.RenamedFromByPath,
            toolStats);

        return new SessionUsefulnessReport(
            rating,
            sessionFile,
            suggestedFiles,
            suggestedTests,
            changes.ChangedFiles,
            changedTests,
            runTests,
            fileCredits.DerivedSuggestedFiles,
            fileCredits.DerivedChangedFiles,
            fileCredits.UnrelatedChangedFiles,
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
        int suggestedFilesCredited,
        int suggestedTests,
        int suggestedTestsChangedOrRun,
        int graphCalls,
        int manualFallbackCommands,
        int staleWarnings)
    {
        if (graphCalls == 0 || suggestedFiles == 0)
            return "unknown";

        var fileRatio = suggestedFilesCredited / (double)suggestedFiles;
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

    private static DerivedMatchHint? CreateDerivedMatchHint(
        SessionEvidenceEvent item,
        IReadOnlyList<string> normalizedFiles)
    {
        var derivedFromFiles = item.DerivedFromFiles
            .Select(SessionPathNormalizer.Normalize)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var plannedFolders = item.PlannedFolders
            .Select(NormalizeFolder)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var plannedNamespaces = item.PlannedNamespaces
            .Select(NormalizeNamespace)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (derivedFromFiles.Length == 0 && plannedFolders.Length == 0 && plannedNamespaces.Length == 0)
            return null;

        var sourceFiles = derivedFromFiles.Length > 0
            ? derivedFromFiles
            : normalizedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targetFiles = derivedFromFiles.Length > 0
            ? normalizedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (sourceFiles.Length == 0)
            return null;

        return new DerivedMatchHint(
            sourceFiles,
            targetFiles,
            plannedFolders,
            plannedNamespaces,
            NormalizeChangeKind(item.ChangeKind));
    }

    private static FileCreditSummary EvaluateFileCredits(
        DirectoryInfo root,
        IReadOnlySet<string> changedFiles,
        IReadOnlySet<string> suggestedFiles,
        IReadOnlyList<DerivedMatchHint> derivedHints,
        IReadOnlyDictionary<string, string> renamedFromByPath)
    {
        var derivedSuggestedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var derivedChangedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var derivedPathsBySuggestedFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var namespaceCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var changedFile in changedFiles)
        {
            if (suggestedFiles.Contains(changedFile))
                continue;

            if (renamedFromByPath.TryGetValue(changedFile, out var originalPath)
                && suggestedFiles.Contains(originalPath))
            {
                RegisterDerivedMatch(originalPath, changedFile);
                continue;
            }

            foreach (var hint in derivedHints)
            {
                if (!MatchesDerivedHint(root, changedFile, hint, namespaceCache))
                    continue;

                foreach (var sourceFile in hint.SourceFiles)
                {
                    if (suggestedFiles.Contains(sourceFile))
                        RegisterDerivedMatch(sourceFile, changedFile);
                }
            }
        }

        var unrelatedChangedFiles = changedFiles
            .Where(path => !suggestedFiles.Contains(path) && !derivedChangedFiles.Contains(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new FileCreditSummary(
            derivedSuggestedFiles,
            derivedChangedFiles,
            unrelatedChangedFiles,
            derivedPathsBySuggestedFile.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlySet<string>)entry.Value,
                StringComparer.OrdinalIgnoreCase));

        void RegisterDerivedMatch(string sourceFile, string changedFile)
        {
            if (!derivedPathsBySuggestedFile.TryGetValue(sourceFile, out var derivedPaths))
            {
                derivedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                derivedPathsBySuggestedFile[sourceFile] = derivedPaths;
            }

            derivedPaths.Add(changedFile);
            derivedChangedFiles.Add(changedFile);

            if (!changedFiles.Contains(sourceFile))
                derivedSuggestedFiles.Add(sourceFile);
        }
    }

    private static bool MatchesDerivedHint(
        DirectoryInfo root,
        string changedFile,
        DerivedMatchHint hint,
        IDictionary<string, string?> namespaceCache)
    {
        if (hint.TargetFiles.Contains(changedFile))
            return true;

        if (hint.PlannedFolders.Any(folder => IsUnderFolder(changedFile, folder)))
            return true;

        if (hint.PlannedNamespaces.Count == 0)
            return false;

        var declaredNamespace = GetDeclaredNamespace(root, changedFile, namespaceCache);
        if (string.IsNullOrWhiteSpace(declaredNamespace))
            return false;

        return hint.PlannedNamespaces.Any(plannedNamespace =>
            declaredNamespace.Equals(plannedNamespace, StringComparison.OrdinalIgnoreCase)
            || declaredNamespace.StartsWith($"{plannedNamespace}.", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeFolder(string? folder)
    {
        var normalized = SessionPathNormalizer.Normalize(folder);
        return normalized.TrimEnd('/');
    }

    private static string NormalizeNamespace(string? namespaceValue) =>
        string.IsNullOrWhiteSpace(namespaceValue)
            ? string.Empty
            : namespaceValue.Trim();

    private static string NormalizeChangeKind(string? changeKind) =>
        string.IsNullOrWhiteSpace(changeKind)
            ? string.Empty
            : changeKind.Trim().ToLowerInvariant();

    private static bool IsUnderFolder(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{folder}/", StringComparison.OrdinalIgnoreCase);

    private static string? GetDeclaredNamespace(
        DirectoryInfo root,
        string changedFile,
        IDictionary<string, string?> namespaceCache)
    {
        if (namespaceCache.TryGetValue(changedFile, out var cached))
            return cached;

        var fullPath = Path.Combine(root.FullName, changedFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            namespaceCache[changedFile] = null;
            return null;
        }

        foreach (var line in File.ReadLines(fullPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                continue;

            var namespaceValue = trimmed["namespace ".Length..].Trim();
            if (namespaceValue.EndsWith(";", StringComparison.Ordinal))
                namespaceValue = namespaceValue[..^1].Trim();

            var firstDelimiter = namespaceValue.IndexOfAny([' ', '{']);
            if (firstDelimiter >= 0)
                namespaceValue = namespaceValue[..firstDelimiter].Trim();

            namespaceCache[changedFile] = namespaceValue.Length == 0 ? null : namespaceValue;
            return namespaceCache[changedFile];
        }

        namespaceCache[changedFile] = null;
        return null;
    }

    private static SessionPrecisionFeedback BuildPrecisionFeedback(
        DirectoryInfo root,
        string? project,
        FileInfo sessionFile,
        IReadOnlySet<string> changedTests,
        IReadOnlySet<string> changedFiles,
        IReadOnlyDictionary<string, string> renamedFromByPath,
        IReadOnlyDictionary<string, ToolPrecisionBuilder> toolStats)
    {
        var tools = toolStats.Values
            .OrderBy(builder => builder.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(builder => builder.Build(root, changedFiles, changedTests, renamedFromByPath))
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
        public List<DerivedMatchHint> DerivedHints { get; } = [];
        public int GraphCalls { get; set; }
        public int ExactTargets { get; set; }
        public int FileOnlyTargets { get; set; }
        public int HeuristicTargets { get; set; }
        public int StaleTargets { get; set; }
        public int StaleWarnings { get; set; }
        public int ManualFallbackCommands { get; set; }

        public ToolPrecisionFeedback Build(
            DirectoryInfo root,
            IReadOnlySet<string> changedFiles,
            IReadOnlySet<string> changedTests,
            IReadOnlyDictionary<string, string> renamedFromByPath)
        {
            var fileCredits = EvaluateFileCredits(root, changedFiles, SuggestedFiles, DerivedHints, renamedFromByPath);
            var fileSignals = SuggestedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var directAccepted = changedFiles.Contains(path) ? 1 : 0;
                    var derivedPaths = fileCredits.DerivedPathsBySuggestedFile.TryGetValue(path, out var paths)
                        ? paths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()
                        : [];
                    var derivedAccepted = derivedPaths.Length > 0 ? 1 : 0;

                    return new PathPrecisionFeedback(
                        path,
                        1,
                        directAccepted,
                        derivedAccepted,
                        directAccepted == 0 && derivedAccepted == 0 ? 1 : 0,
                        derivedPaths);
                })
                .ToArray();
            var testSignals = SuggestedTests
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new PathPrecisionFeedback(
                    path,
                    1,
                    changedTests.Contains(path) || RunTests.Contains(path) ? 1 : 0,
                    0,
                    changedTests.Contains(path) || RunTests.Contains(path) ? 0 : 1,
                    []))
                .ToArray();

            return new ToolPrecisionFeedback(
                ToolName,
                fileSignals.Length,
                fileSignals.Sum(signal => signal.AcceptedCount),
                fileSignals.Sum(signal => signal.DerivedAcceptedCount),
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

    private sealed record DerivedMatchHint(
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<string> TargetFiles,
        IReadOnlyList<string> PlannedFolders,
        IReadOnlyList<string> PlannedNamespaces,
        string ChangeKind);

    private sealed record FileCreditSummary(
        IReadOnlySet<string> DerivedSuggestedFiles,
        IReadOnlySet<string> DerivedChangedFiles,
        IReadOnlySet<string> UnrelatedChangedFiles,
        IReadOnlyDictionary<string, IReadOnlySet<string>> DerivedPathsBySuggestedFile);
}
