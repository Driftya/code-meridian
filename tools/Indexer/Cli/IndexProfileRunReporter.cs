using System.Net.Http.Headers;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexProfileRunReporter(LocalProjectProfileBuilder builder)
{
    internal Func<string, string?, HttpClient> HttpClientFactory { get; init; } = CreateHttpClient;
    internal Func<string, CancellationToken, Task<ProjectAnalyzerFingerprint?>> WorkerFingerprintReader { get; init; } = PowerShellWorkerProcess.ReadFingerprintAsync;

    public async Task<bool> IsCacheCurrentAsync(ResolvedIndexerSettings settings, IncrementalIndexCache cache,
        CancellationToken cancellationToken = default, bool verifyInventory = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cache.HasPublicationFor(settings.CodeMeridianUrl)) return false;
        using var http = HttpClientFactory(settings.CodeMeridianUrl, settings.ApiKey);
        var profile = await new CodeMeridianClient(http).GetProjectProfileAsync(settings.Project, cancellationToken: cancellationToken);
        var matches = profile.ContractVersion == "1.0" && profile.Project == settings.Project && profile.Status == "available"
            && profile.Generation == profile.LatestGeneration
            && profile.Profile is { ValueKind: System.Text.Json.JsonValueKind.Object } details
            && details.TryGetProperty("observedAt", out var observation)
            && observation.ValueKind == System.Text.Json.JsonValueKind.String && observation.TryGetDateTimeOffset(out var observedAt)
            && cache.MatchesPublication(settings.CodeMeridianUrl, profile.Generation, observedAt)
            && await AnalysisMetadataMatchesAsync(details, settings, cancellationToken);
        if (!matches || !verifyInventory) return matches;
        // A no-change selection must also reconcile policy, assets, disabled languages and exclusions.
        var snapshot = builder.BuildSnapshot(settings, profile.Generation!.Value, out var inventory, cancellationToken);
        if (!inventory.IsComplete) return false;
        var source = AnalyzedSourceSnapshot.Capture(settings.RootPath, snapshot.Files, cancellationToken);
        if (source.Fingerprint != profile.Profile!.Value.GetProperty("sourceFingerprint").GetString()) return false;
        snapshot = snapshot with { Profile = snapshot.Profile with { SourceFingerprint = source.Fingerprint } };
        var evidence = await ConstraintEvidenceBuilder.BuildAsync(settings, snapshot, inventory, source, cancellationToken);
        var published = profile.RepositoryConstraints is { ValueKind: JsonValueKind.Object } constraints
            && constraints.TryGetProperty("evidenceFingerprint", out var fingerprint) && fingerprint.ValueKind == JsonValueKind.String
            ? fingerprint.GetString() : null;
        return evidence?.Fingerprint == published && source.IsCurrent(cancellationToken);
    }

    private async Task<bool> AnalysisMetadataMatchesAsync(JsonElement profile, ResolvedIndexerSettings settings, CancellationToken cancellationToken)
    {
        if (!profile.TryGetProperty("configurationFingerprint", out var configuration)
            || configuration.ValueKind != JsonValueKind.String
            || configuration.GetString() != ProjectAnalysisFingerprints.Configuration(settings)
            || !profile.TryGetProperty("analyzers", out var analyzers) || analyzers.ValueKind != JsonValueKind.Array)
            return false;
        if (!profile.TryGetProperty("sourceFingerprint", out var source) || source.ValueKind != JsonValueKind.String
            || source.GetString() is not { Length: 71 } sourceHash || !sourceHash.StartsWith("sha256:", StringComparison.Ordinal)
            || !sourceHash.Skip(7).All(char.IsAsciiHexDigit)) return false;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var published = analyzers.Deserialize<ProjectAnalyzerProfile[]>(options);
        if (published is null) return false;
        foreach (var expected in LocalProjectProfileBuilder.BuildAnalyzers(settings).Where(analyzer => analyzer.Supported && analyzer.ConfigurationState == "enabled"))
        {
            var fingerprint = IsWorker(expected.Id)
                ? await WorkerFingerprintReader(expected.Id, cancellationToken) : expected.Fingerprint;
            var matches = published.Where(analyzer => analyzer?.Id == expected.Id).ToArray();
            if (fingerprint is null || matches.Length != 1 || matches[0] is not { Supported: true, ConfigurationState: "enabled" }
                || matches[0].Fingerprint != fingerprint) return false;
        }
        var revision = profile.TryGetProperty("revision", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            ? metadata.Deserialize<ProjectRepositoryRevision>(options) : null;
        // Dirty state is observational; ordinary local edits must still be eligible for a delta selection.
        return revision?.Commit == ProjectRevisionReader.Read(settings.RootPath, cancellationToken)?.Commit;
    }

    public async Task<int> RunAsync(ResolvedIndexerSettings settings, Func<IndexProfileRun?, Task<int>> run,
        CancellationToken cancellationToken = default)
    {
        if (settings.ExternalOnly)
            return await run(null); // External reference scope is outside this repository inventory.
        try
        {
            using var http = HttpClientFactory(settings.CodeMeridianUrl, settings.ApiKey);
            var client = new CodeMeridianClient(http);
            var generation = await client.BeginProjectProfileAsync(settings.Project, cancellationToken);
            var snapshot = builder.BuildSnapshot(settings, generation, out var inventory, cancellationToken);
            if (snapshot.Profile.DiscoveryState != "complete")
            {
                Console.Error.WriteLine("Discovery is partial; indexing was not started and the previous complete profile was retained.");
                return 2;
            }
            var analyzers = snapshot.Profile.Analyzers.ToArray();
            for (var index = 0; index < analyzers.Length; index++)
            {
                var analyzer = analyzers[index];
                if (IsWorker(analyzer.Id) && analyzer.ConfigurationState == "enabled")
                    analyzers[index] = analyzer with { Fingerprint = await WorkerFingerprintReader(analyzer.Id, cancellationToken) };
            }
            var source = AnalyzedSourceSnapshot.Capture(settings.RootPath, snapshot.Files, cancellationToken);
            snapshot = snapshot with { Profile = snapshot.Profile with { Analyzers = analyzers, SourceFingerprint = source.Fingerprint } };
            snapshot = await ConstraintEvidenceBuilder.DescribeArchitectureAsync(settings, snapshot, source, cancellationToken);
            snapshot = snapshot with { ConstraintEvidence = await ConstraintEvidenceBuilder.BuildAsync(settings, snapshot, inventory, source, cancellationToken) };
            if (settings.IncludeDocs)
                snapshot = snapshot with { MarkdownEvidence = await MarkdownEvidenceBuilder.BuildAsync(snapshot, settings.RootPath, source, cancellationToken) };
            if (!source.IsCurrent(cancellationToken)) return 2;
            if (snapshot.Validate() is { } error) throw new InvalidOperationException(error);
            await client.PublishProjectProfileAsync(snapshot, cancellationToken);
            if (!source.IsCurrent(cancellationToken))
            {
                await client.BeginProjectProfileAsync(settings.Project, cancellationToken);
                return 2;
            }
            var caseSensitive = PowerShellIndexRunCoordinator.IsEnabled(settings)
                ? DirectoryCaseSensitivity.Read(settings.RootPath, inventory.Directories, cancellationToken) : null;
            if (caseSensitive is { } knownCase) source.BindCaseSensitivity(knownCase);
            var report = new IndexProfileRun(client, snapshot, source)
            {
                CaseSensitive = caseSensitive
            };
            int exitCode;
            try { exitCode = await run(report); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await report.CompleteAsync(1, cancellationToken);
                throw;
            }
            return await report.CompleteAsync(exitCode, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Indexing or profile publication failed: {ex.Message}");
            return 1;
        }
    }

    private static bool IsWorker(string analyzer) => analyzer == "powershell" || NodeWorkerIdentity.IsWorker(analyzer);

    private static HttpClient CreateHttpClient(string url, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
