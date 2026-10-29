using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;
using CodeMeridian.RoslynIndexer.Pipeline;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexProfileRun(CodeMeridianClient client, ProjectProfileSnapshot snapshot, AnalyzedSourceSnapshot source)
{
    private static readonly string ReporterVersion = "cli/" + typeof(IndexProfileRun).Assembly.GetName().Version;
    private readonly List<ProjectAnalyzerOutcome> _terminal = [];
    private bool powerShellDefinitionsPublished;
    private readonly List<ProducerExecutionReport> producerReports = [];

    public string Project => snapshot.Profile.Project;
    public string[] ProducerPaths(string analyzer) => snapshot.Files.Where(file => analyzer switch
    {
        "sql" => file.Kind == "sql", "typescript" => file.Kind is "typescript" or "javascript",
        "frontend" => file.Kind is "html" or "css" or "scss" || file.Kind is "typescript" or "javascript"
            && Path.GetExtension(file.Path).ToLowerInvariant() is ".tsx" or ".jsx",
        "configuration" => file.Kind == "configuration", _ => false
    }).Select(file => file.Path).ToArray();

    public void AcceptProducerReport(ProducerExecutionReport report)
    {
        if (report.Project != Project || report.Scope != "." && ProjectProfilePath.Normalize(report.Scope) != report.Scope
            || report.Fingerprint is null || report.Fingerprint != Fingerprint(report.Analyzer))
            throw new InvalidDataException("Producer project, scope or identity does not match the current pass.");
        if (report.Analyzer == "sql" && (report.Generation != Generation || report.SourceFingerprint != SourceFingerprint))
            throw new InvalidDataException("SQL publication receipt is not bound to the current inventory generation.");
        var expected = ProducerPaths(report.Analyzer).ToHashSet(StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in report.Files)
        {
            if (ProjectProfilePath.Normalize(file.Path) != file.Path || !expected.Contains(file.Path) || !observed.Add(file.Path)
                || report.Scope != "." && !file.Path.StartsWith(report.Scope + "/", StringComparison.Ordinal)
                || file.Status != "failed" && file.SourceFingerprint != source.FileFingerprint(file.Path))
                throw new InvalidDataException("Producer source reads do not match the captured inventory or scope.");
        }
        if (report.Fingerprint != Fingerprint(report.Analyzer) || producerReports.Any(item => item.Analyzer == report.Analyzer && item.Scope == report.Scope))
            throw new InvalidDataException("Producer identity changed or a scope was reported twice.");
        producerReports.Add(report);
    }

    public Task<string> ReadSourceAsync(FileInfo file, CancellationToken cancellationToken) => source.ReadSourceAsync(file, cancellationToken);
    public Task<string> ReadStructuralSourceAsync(FileInfo file, CancellationToken cancellationToken) => source.ReadStructuralSourceAsync(file, cancellationToken);
    public string SourceFileFingerprint(string path) => source.FileFingerprint(path);

    public long Generation => snapshot.Generation;
    public DateTimeOffset ObservedAt => snapshot.Profile.ObservedAt;
    public string SourceFingerprint => source.Fingerprint;
    public bool? CaseSensitive { get; init; }
    public string[] PowerShellPaths => snapshot.Files.Where(file => file.Kind == "powershell").Select(file => file.Path).ToArray();
    public async Task PublishPowerShellAsync(CodeMeridian.Core.CodeGraph.PowerShellGraphSnapshot graph, CancellationToken cancellationToken)
    {
        if (graph.ValidateAgainst(snapshot.Profile, snapshot.Files) is { } error) throw new InvalidDataException(error);
        foreach (var file in graph.Files.Where(file => file.Status != "failed"))
            if (file.SourceFingerprint != source.FileFingerprint(file.Path))
                throw new InvalidDataException("PowerShell worker read different bytes than the captured source inventory.");
        if (!source.IsCurrent(cancellationToken)) throw new InvalidDataException("Sources changed before PowerShell publication.");
        await client.PublishPowerShellGraphAsync(graph, cancellationToken);
        powerShellDefinitionsPublished = graph.Files.All(file => file.Status == "complete");
    }
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
        ProducerExecutionReport[] producers = [];
        var producerComplete = false;
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
            : (analyzer is "documents" or "csharp" or "powershell" or "sql" or "typescript" or "frontend" or "configuration") && exitCode == 2 ? "partial" : "failed";
        producers = producerReports.Where(report => report.Analyzer == analyzer).ToArray();
        var selected = producers.SelectMany(report => report.Files).ToArray();
        var producerVerified = producers.Length > 0 && producers.All(report => report.Published)
            && selected.Select(file => file.Path).Order(StringComparer.Ordinal).SequenceEqual(ProducerPaths(analyzer).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        var declared = snapshot.Profile.Analyzers.Single(item => item.Id == analyzer).SupportedCapabilities;
        producerComplete = producerVerified && selected.All(file => file.Status == "complete")
            && declared.Count > 0
            && producers.All(report => declared.All(capability => report.Capabilities.GetValueOrDefault(capability) == "complete"));
        if (state == "succeeded" && (producers.Any(report => !report.Published || report.Files.Any(file => file.Status != "complete"))
            || fullSelection && producers.Length > 0 && !producerVerified)) state = "partial";
        // Verified source reads alone do not audit C# project membership or callable semantics.
        var sourceVerified = analyzer == "documents" && source.AllDocumentsRead
            || analyzer == "powershell" && powerShellDefinitionsPublished;
        if (exitCode == 0 && analyzer == "documents" && fullSelection && verifiedCoverage && !sourceVerified)
        {
            Record("partial", "partial", ["Not every document in the captured inventory was read with verified source bytes."]);
            return 2;
        }
        var coverage = state == "succeeded" && fullSelection
            && (sourceVerified && (verifiedCoverage || analyzer == "powershell") || producerComplete) ? "complete"
            : state == "partial" ? "partial" : "unknown";
        Record(state, coverage, producers.Length > 0 ? producers.SelectMany(report => report.Limitations.Concat(
                [$"Producer scope {report.Scope}: {report.Files.Count} selected file outcomes; publication={report.Published}."]))
                .Distinct().Take(16).ToArray() : coverage == "complete" ? [] : worker?.Limitations.ToArray() ??
            ["Process completion does not establish complete target coverage. Delta selection and unaudited worker outcomes remain insufficient for readiness."]);
        return exitCode == 0 && state == "partial" && producers.Length > 0 ? 2 : exitCode;

        async Task PublishAsync(string state, string coverage, string[] limitations)
        {
            await PublishOutcomesAsync(Outcomes(state, coverage, limitations), cancellationToken);
        }

        void Record(string state, string coverage, string[] limitations)
        {
            var terminalScopes = scopes.Concat(producers.Select(report => report.Scope)).Distinct(StringComparer.Ordinal).ToArray();
            if (producerComplete && fullSelection) terminalScopes = terminalScopes.Append(".").Distinct(StringComparer.Ordinal).ToArray();
            _terminal.AddRange(terminalScopes.Select(scope => new ProjectAnalyzerOutcome(analyzer, scope, state, coverage, mode,
                DateTimeOffset.UtcNow, ReporterVersion, limitations)
                { Fingerprint = Fingerprint(analyzer), SourceFingerprint = source.Fingerprint }));
        }

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
        if (!current || !source.IsCurrent(cancellationToken))
        {
            // Invalidate the profile's structural facts as well as its immutable terminal outcomes.
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
