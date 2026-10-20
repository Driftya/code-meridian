using System.Net.Http.Headers;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexProfileRunReporter(LocalProjectProfileBuilder builder)
{
    internal Func<string, string?, HttpClient> HttpClientFactory { get; init; } = CreateHttpClient;

    public async Task<bool> IsCacheCurrentAsync(ResolvedIndexerSettings settings, IncrementalIndexCache cache,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cache.HasPublicationFor(settings.CodeMeridianUrl)) return false;
        using var http = HttpClientFactory(settings.CodeMeridianUrl, settings.ApiKey);
        var profile = await new CodeMeridianClient(http).GetProjectProfileAsync(settings.Project, cancellationToken: cancellationToken);
        return profile.ContractVersion == "1.0" && profile.Project == settings.Project && profile.Status == "available"
            && profile.Generation == profile.LatestGeneration
            && profile.Profile is { ValueKind: System.Text.Json.JsonValueKind.Object } details
            && details.TryGetProperty("observedAt", out var observation)
            && observation.ValueKind == System.Text.Json.JsonValueKind.String && observation.TryGetDateTimeOffset(out var observedAt)
            && cache.MatchesPublication(settings.CodeMeridianUrl, profile.Generation, observedAt)
            && AnalysisMetadataMatches(details, settings, cancellationToken);
    }

    private static bool AnalysisMetadataMatches(JsonElement profile, ResolvedIndexerSettings settings, CancellationToken cancellationToken)
    {
        if (!profile.TryGetProperty("configurationFingerprint", out var configuration)
            || configuration.ValueKind != JsonValueKind.String
            || configuration.GetString() != ProjectAnalysisFingerprints.Configuration(settings)
            || !profile.TryGetProperty("analyzers", out var analyzers) || analyzers.ValueKind != JsonValueKind.Array)
            return false;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var published = analyzers.Deserialize<ProjectAnalyzerProfile[]>(options);
        if (published is null) return false;
        foreach (var expected in LocalProjectProfileBuilder.BuildAnalyzers(settings).Where(analyzer => analyzer.Supported && analyzer.ConfigurationState == "enabled"))
        {
            var matches = published.Where(analyzer => analyzer?.Id == expected.Id).ToArray();
            if (matches.Length != 1 || matches[0] is not { Supported: true, ConfigurationState: "enabled" }
                || matches[0].Fingerprint != expected.Fingerprint) return false;
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
            var snapshot = builder.BuildSnapshot(settings, generation, cancellationToken);
            if (snapshot.Profile.DiscoveryState != "complete")
            {
                Console.Error.WriteLine("Discovery is partial; indexing was not started and the previous complete profile was retained.");
                return 2;
            }
            if (snapshot.Validate() is { } error) throw new InvalidOperationException(error);
            await client.PublishProjectProfileAsync(snapshot, cancellationToken);
            return await run(new IndexProfileRun(client, snapshot));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Indexing or profile publication failed: {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateHttpClient(string url, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
