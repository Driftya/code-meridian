using System.Net.Http.Headers;
using System.Text.Json;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class TypeScriptScopeCatalogWriter
{
    private readonly Func<string, string?, HttpClient> httpClientFactory;

    public TypeScriptScopeCatalogWriter()
        : this(CreateHttpClient)
    {
    }

    internal TypeScriptScopeCatalogWriter(Func<string, string?, HttpClient> httpClientFactory) =>
        this.httpClientFactory = httpClientFactory;

    public async Task WriteAsync(
        string project,
        string codeMeridianUrl,
        string? apiKey,
        IEnumerable<DirectoryInfo> roots,
        CancellationToken cancellationToken = default)
    {
        var resolutionScopes = NormalizeScopes(roots);
        using var httpClient = httpClientFactory(codeMeridianUrl, apiKey);
        var client = new CodeMeridianClient(httpClient);
        await client.IngestCodeNodeAsync(
            $"{project}::IndexScopeCatalog::typescript",
            "TypeScript relationship scope catalog",
            "Diagnostic",
            summary: $"Tracks {resolutionScopes.Count} active TypeScript relationship-resolution scope(s).",
            projectContext: project,
            properties: new Dictionary<string, string>
            {
                ["externalKind"] = "IndexRun",
                ["indexRunKind"] = "IndexScopeCatalog",
                ["language"] = "TypeScript",
                ["resolutionScopes"] = JsonSerializer.Serialize(resolutionScopes),
                ["completedAt"] = DateTimeOffset.UtcNow.ToString("O")
            },
            cancellationToken: cancellationToken);
    }

    internal static IReadOnlyList<string> NormalizeScopes(IEnumerable<DirectoryInfo> roots) =>
        roots
            .Select(root => Path.GetFullPath(root.FullName).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static HttpClient CreateHttpClient(string codeMeridianUrl, string? apiKey)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
