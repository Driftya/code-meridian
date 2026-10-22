using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;
using CodeMeridian.RoslynIndexer.Pipeline;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexProfileRun(CodeMeridianClient client, ProjectProfileSnapshot snapshot, AnalyzedSourceSnapshot source)
{
    private static readonly string ReporterVersion = "cli/" + typeof(IndexProfileRun).Assembly.GetName().Version;
    private readonly List<ProjectAnalyzerOutcome> _terminal = [];

    public Task<string> ReadSourceAsync(FileInfo file, CancellationToken cancellationToken) => source.ReadSourceAsync(file, cancellationToken);

    public long Generation => snapshot.Generation;
    public DateTimeOffset ObservedAt => snapshot.Profile.ObservedAt;
    public ProjectAnalyzerFingerprint? Fingerprint(string analyzer) => snapshot.Profile.Analyzers.Single(item => item.Id == analyzer).Fingerprint;

    public bool CoversAllDocuments(IReadOnlyCollection<string>? changedFiles) => changedFiles is null
        || snapshot.Files.Where(file => file.Kind is "markdown" or "text")
            .All(file => changedFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase));

    public async Task<int> RunCSharpAnalyzerAsync(Func<Task<IndexStats>> run, bool fullSelection,
        CancellationToken cancellationToken = default)
    {
        CSharpAnalysisReport? analysis = null;
        return await RunAnalyzerCoreAsync("csharp", ["."], async () =>
        {
            analysis = (await run()).Analysis;
            return analysis?.ExitCode ?? 2;
        }, fullSelection, false, () => analysis, cancellationToken);
    }

    public Task<int> RunAnalyzerAsync(string analyzer, IReadOnlyList<string> scopes, Func<Task<int>> run,
        bool fullSelection, bool verifiedCoverage = false, CancellationToken cancellationToken = default)
        => RunAnalyzerCoreAsync(analyzer, scopes, run, fullSelection, verifiedCoverage, null, cancellationToken);

    private async Task<int> RunAnalyzerCoreAsync(string analyzer, IReadOnlyList<string> scopes, Func<Task<int>> run,
        bool fullSelection, bool verifiedCoverage, Func<CSharpAnalysisReport?>? workerReport, CancellationToken cancellationToken)
    {
        var mode = fullSelection ? "full" : "delta";
        await PublishAsync("running", "unknown", []);
        if (!source.IsCurrent(cancellationToken))
        {
            Record("failed", "unknown", ["Source content or inventory changed before analysis."]);
            return 2;
        }
        int exitCode;
        try { exitCode = await run(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Record("failed", "unknown", ["Analyzer execution failed; retained semantic facts are not current coverage evidence."]);
            throw;
        }
        if (!source.IsCurrent(cancellationToken))
        {
            Record("failed", "unknown", ["Source content or inventory changed during analysis."]);
            return 2;
        }
        var worker = workerReport?.Invoke();
        var state = exitCode == 0 ? worker?.Coverage == "partial" ? "partial" : "succeeded"
            : (analyzer is "documents" or "csharp") && exitCode == 2 ? "partial" : "failed";
        // Verified source reads alone do not audit C# project membership or callable semantics.
        var sourceVerified = analyzer == "documents" && source.AllDocumentsRead;
        if (exitCode == 0 && analyzer == "documents" && fullSelection && verifiedCoverage && !sourceVerified)
        {
            Record("partial", "partial", ["Not every document in the captured inventory was read with verified source bytes."]);
            return 2;
        }
        var coverage = state == "succeeded" && fullSelection && verifiedCoverage && sourceVerified ? "complete"
            : state == "partial" ? "partial" : "unknown";
        Record(state, coverage, coverage == "complete" ? [] : worker?.Limitations.ToArray() ??
            ["Process completion does not establish complete target coverage. Delta selection and unaudited worker outcomes remain insufficient for readiness."]);
        return exitCode;

        async Task PublishAsync(string state, string coverage, string[] limitations)
        {
            await PublishOutcomesAsync(Outcomes(state, coverage, limitations), cancellationToken);
        }

        void Record(string state, string coverage, string[] limitations) => _terminal.AddRange(Outcomes(state, coverage, limitations));

        ProjectAnalyzerOutcome[] Outcomes(string state, string coverage, string[] limitations) =>
            scopes.Distinct(StringComparer.Ordinal).Select(scope =>
                new ProjectAnalyzerOutcome(analyzer, scope, state, coverage, mode,
                    DateTimeOffset.UtcNow, ReporterVersion, limitations)
                { Fingerprint = Fingerprint(analyzer), SourceFingerprint = source.Fingerprint }).ToArray();
    }

    public async Task<int> CompleteAsync(int exitCode, CancellationToken cancellationToken)
    {
        var current = source.IsCurrent(cancellationToken);
        var outcomes = current ? _terminal.ToArray() : _terminal.Select(outcome => outcome with
        {
            State = "failed", Coverage = "unknown", ObservedAt = DateTimeOffset.UtcNow,
            Limitations = ["Source content or inventory changed during the index pass; no complete evidence was published."]
        }).ToArray();
        if (outcomes.Length > 0) await PublishOutcomesAsync(outcomes, cancellationToken);
        if (current && !source.IsCurrent(cancellationToken))
        {
            // Terminal outcomes are immutable. A change while uploading must make this generation stale.
            await client.BeginProjectProfileAsync(snapshot.Profile.Project, cancellationToken);
            return 2;
        }
        return current ? exitCode : 2;
    }

    private async Task PublishOutcomesAsync(ProjectAnalyzerOutcome[] outcomes, CancellationToken cancellationToken)
    {
        var report = new ProjectAnalyzerOutcomeSnapshot(snapshot.Profile.Project, snapshot.Generation, outcomes);
        if (report.Validate() is { } error) throw new InvalidOperationException(error);
        await client.PublishProjectAnalyzerOutcomesAsync(report, cancellationToken);
    }
}
