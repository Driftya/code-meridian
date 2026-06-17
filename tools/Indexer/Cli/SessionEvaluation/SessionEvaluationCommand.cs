using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class SessionEvaluationCommand(
    IToolConfigurationService configurationService,
    SessionUsefulnessEvaluator evaluator)
{
    public async Task<int> RunAsync(string? path, string? project, string? session, string gitBase)
    {
        var context = configurationService.CreateContext(path);
        var resolvedProject = configurationService.ResolveProject(context, project);
        var sessionPath = ResolveSessionPath(context.RootPath, session);

        SessionUsefulnessReport report;
        try
        {
            report = await evaluator.EvaluateAsync(new SessionEvaluationOptions(
                context.RootPath,
                resolvedProject,
                sessionPath,
                gitBase));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        Print(report);
        return 0;
    }

    private static FileSystemInfo? ResolveSessionPath(DirectoryInfo root, string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return null;

        var fullPath = Path.GetFullPath(session, root.FullName);
        if (Directory.Exists(fullPath))
            return new DirectoryInfo(fullPath);

        return new FileInfo(fullPath);
    }

    private static void Print(SessionUsefulnessReport report)
    {
        Console.WriteLine($"CodeMeridian usefulness: {report.Rating}");
        Console.WriteLine($"Session evidence: {report.SessionFile.FullName}");
        Console.WriteLine($"Suggested files edited: {report.SuggestedFilesEdited}/{report.SuggestedFiles.Count}");
        Console.WriteLine($"Suggested tests changed/run: {report.SuggestedTestsChangedOrRun}/{report.SuggestedTests.Count}");
        Console.WriteLine($"Graph calls used: {report.GraphCalls}");
        Console.WriteLine($"Exact targets used: {report.ExactTargets}");
        Console.WriteLine($"File-only targets: {report.FileOnlyTargets}");
        Console.WriteLine($"Heuristic targets verified manually: {report.HeuristicTargets}");
        Console.WriteLine($"Stale targets: {report.StaleTargets}");
        Console.WriteLine($"Stale warnings: {report.StaleWarnings}");
        Console.WriteLine($"Manual fallback commands after graph lookup: {report.ManualFallbackCommands}");

        if (report.Notes.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Notes:");
        foreach (var note in report.Notes)
            Console.WriteLine($"- {note}");
    }
}
