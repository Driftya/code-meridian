using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexProfileRun(CodeMeridianClient client, ProjectProfileSnapshot snapshot)
{
    private static readonly string ReporterVersion = "cli/" + typeof(IndexProfileRun).Assembly.GetName().Version;

    public long Generation => snapshot.Generation;
    public DateTimeOffset ObservedAt => snapshot.Profile.ObservedAt;

    public bool CoversAllDocuments(IReadOnlyCollection<string>? changedFiles) => changedFiles is null
        || snapshot.Files.Where(file => file.Kind is "markdown" or "text")
            .All(file => changedFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase));

    public async Task<int> RunAnalyzerAsync(string analyzer, IReadOnlyList<string> scopes, Func<Task<int>> run,
        bool fullSelection, bool verifiedCoverage = false, CancellationToken cancellationToken = default)
    {
        var mode = fullSelection ? "full" : "delta";
        await PublishAsync("running", "unknown", []);
        int exitCode;
        try { exitCode = await run(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await PublishAsync("failed", "unknown", ["Analyzer execution failed; retained semantic facts are not current coverage evidence."]);
            throw;
        }
        var state = exitCode == 0 ? "succeeded" : analyzer == "documents" && exitCode == 2 ? "partial" : "failed";
        var coverage = state == "succeeded" && fullSelection && verifiedCoverage ? "complete"
            : state == "partial" ? "partial" : "unknown";
        await PublishAsync(state, coverage, coverage == "complete" ? [] :
            ["Process completion does not establish complete target coverage. Delta selection and unaudited worker outcomes remain insufficient for readiness."]);
        return exitCode;

        async Task PublishAsync(string state, string coverage, string[] limitations)
        {
            var outcomes = scopes.Distinct(StringComparer.Ordinal).Select(scope =>
                new ProjectAnalyzerOutcome(analyzer, scope, state, coverage, mode,
                    DateTimeOffset.UtcNow, ReporterVersion, limitations)
                { Fingerprint = snapshot.Profile.Analyzers.Single(item => item.Id == analyzer).Fingerprint }).ToArray();
            var report = new ProjectAnalyzerOutcomeSnapshot(snapshot.Profile.Project, snapshot.Generation, outcomes);
            if (report.Validate() is { } error) throw new InvalidOperationException(error);
            await client.PublishProjectAnalyzerOutcomesAsync(report, cancellationToken);
        }
    }
}
