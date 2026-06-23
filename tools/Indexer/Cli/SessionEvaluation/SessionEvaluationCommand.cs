using CodeMeridian.Tooling.Configuration;
using System.Text.Json;

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
        WritePrecisionFeedback(context.RootPath, report.PrecisionFeedback);
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
        Console.WriteLine($"Suggested files edited directly: {report.SuggestedFilesEdited}/{report.SuggestedFiles.Count}");
        Console.WriteLine($"Suggested files credited by derivation: {report.DerivedSuggestedFiles.Count}/{report.SuggestedFiles.Count}");
        Console.WriteLine($"Suggested tests changed/run: {report.SuggestedTestsChangedOrRun}/{report.SuggestedTests.Count}");
        Console.WriteLine($"Unrelated changed files: {report.UnrelatedChangedFiles.Count}");
        Console.WriteLine($"Graph calls used: {report.GraphCalls}");
        Console.WriteLine($"Exact targets used: {report.ExactTargets}");
        Console.WriteLine($"File-only targets: {report.FileOnlyTargets}");
        Console.WriteLine($"Heuristic targets verified manually: {report.HeuristicTargets}");
        Console.WriteLine($"Stale targets: {report.StaleTargets}");
        Console.WriteLine($"Stale warnings: {report.StaleWarnings}");
        Console.WriteLine($"Manual fallback commands after graph lookup: {report.ManualFallbackCommands}");
        Console.WriteLine($"Context packs: full {report.ContextPackFullSuccesses}, degraded {report.ContextPackDegradedSuccesses}, hard failure {report.ContextPackHardFailures}");

        if (report.Notes.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Notes:");
        foreach (var note in report.Notes)
            Console.WriteLine($"- {note}");
    }

    private static void WritePrecisionFeedback(DirectoryInfo root, SessionPrecisionFeedback feedback)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root.FullName, ".meridian"));
        var path = Path.Combine(directory.FullName, "precision-feedback.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        File.WriteAllText(path, JsonSerializer.Serialize(feedback, options));
        Console.WriteLine();
        Console.WriteLine($"Precision feedback: {path}");
    }
}
