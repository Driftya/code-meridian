using System.Text.Json;
using System.Net.Http.Headers;
using CodeMeridian.Sdk;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ProjectProfileCommand(
    IndexCommandSettingsFactory settingsFactory,
    LocalProjectProfileBuilder profileBuilder)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal Func<string, string?, HttpClient> HttpClientFactory { get; init; } = CreateHttpClient;

    public async Task<int> RunAsync(string? path, string? project, string format, bool skipCSharp, bool skipTypeScript,
        bool skipDocs, bool skipConfiguration, bool skipSql, bool publish = false, string? url = null,
        CancellationToken cancellationToken = default)
    {
        if (format is not ("text" or "json"))
        {
            Console.Error.WriteLine("error: --format must be text or json.");
            return 1;
        }

        try
        {
            var settings = settingsFactory.Create(new IndexCommandOptions(
                path, project, url, Clear: false, RebuildKeywords: false, IncludeDocs: !skipDocs,
                Watch: false, DryRun: true, ListCapabilities: false, SkipCSharp: skipCSharp,
                SkipTypeScript: skipTypeScript, SkipConfiguration: skipConfiguration,
                SkipDiagnostics: true, AllowRepoScripts: false, Incremental: false, Storage: null, SkipSql: skipSql));
            using var httpClient = publish ? HttpClientFactory(settings.CodeMeridianUrl, settings.ApiKey) : null;
            var client = httpClient is null ? null : new CodeMeridianClient(httpClient);
            var generation = client is null ? 0 : await client.BeginProjectProfileAsync(settings.Project, cancellationToken);
            var snapshot = profileBuilder.BuildSnapshot(settings, generation, cancellationToken);
            var profile = snapshot.Profile;
            if (client is not null && profile.DiscoveryState == "complete")
            {
                if (snapshot.Validate() is { } error) throw new InvalidOperationException(error);
                await client.PublishProjectProfileAsync(snapshot, cancellationToken);
                profile = profile with { EvidenceSource = "uploaded_discovery" };
            }
            else if (client is not null)
                Console.Error.WriteLine("Discovery is partial; the previously published inventory was retained.");
            Console.WriteLine(format == "json" ? JsonSerializer.Serialize(profile, JsonOptions) : ToText(profile));
            return profile.DiscoveryState == "complete" ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or HttpRequestException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateHttpClient(string url, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    internal static string ToText(ProjectProfile profile)
    {
        var lines = new List<string>
        {
            "CodeMeridian project profile",
            $"  Project     : {profile.Project}",
            $"  Evidence    : {profile.EvidenceSource}",
            $"  Discovery   : {profile.DiscoveryState}",
            $"  Files       : {profile.FileCount}",
            $"  Roots       : {profile.AnalysisRootCount} (showing {profile.AnalysisRoots.Count})",
            "  Indexing    : unknown (server evidence has not been queried)",
            "",
            "Observed file kinds:"
        };
        lines.AddRange(profile.FileKinds.Select(kind =>
            $"  {kind.Kind}: {kind.Count} ({string.Join(", ", kind.ExamplePaths)})"));
        if (profile.FileKinds.Count == 0)
            lines.Add("  No files observed within the accepted inventory scope.");
        lines.Add("");
        lines.Add("Analyzer support and configuration (not indexing readiness):");
        lines.AddRange(profile.Analyzers.Select(analyzer =>
            $"  {analyzer.Id}: support={(analyzer.Supported ? "supported" : "unsupported")}, configuration={analyzer.ConfigurationState}, indexing={analyzer.IndexingState}"));
        if (profile.WarningCount > 0)
        {
            lines.Add("");
            lines.Add($"Discovery warnings: {profile.WarningCount} (showing {profile.Warnings.Count})");
            lines.AddRange(profile.Warnings.Select(warning => $"  {warning}"));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
