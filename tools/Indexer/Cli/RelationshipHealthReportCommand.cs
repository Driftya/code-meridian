using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class RelationshipHealthReportCommand
{
    private const string Query = """
        query RelationshipHealth($projectContext: String!) {
          nodes(
            filter: {
              labels: ["CodeNode"]
              projectContext: $projectContext
              propertyEquals: [{ key: "externalKind", value: "IndexRun" }]
            }
            limit: 500
          ) {
            id
            properties { key value }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly Func<string, string?, HttpClient> httpClientFactory;

    public RelationshipHealthReportCommand()
        : this(CreateHttpClient)
    {
    }

    internal RelationshipHealthReportCommand(Func<string, string?, HttpClient> httpClientFactory) =>
        this.httpClientFactory = httpClientFactory;

    public async Task<int> RunAsync(
        string project,
        string codeMeridianUrl,
        string? apiKey,
        string format,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = httpClientFactory(codeMeridianUrl, apiKey);
            using var response = await httpClient.PostAsJsonAsync(
                "graphql",
                new { query = Query, variables = new { projectContext = project } },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<GraphQlEnvelope>(JsonOptions, cancellationToken);
            if (envelope?.Data?.Nodes is null)
                throw new InvalidOperationException(envelope?.Errors?.FirstOrDefault()?.Message ?? "GraphQL returned no data.");

            var rows = envelope.Data.Nodes
                .Select(ParseRun)
                .Where(run => run is not null)
                .Select(run => run!)
                .GroupBy(run => (run.Language, run.Scope, run.Mode))
                .Select(group => group.MaxBy(run => run.CompletedAt)!)
                .OrderBy(run => run.Language, StringComparer.Ordinal)
                .ThenBy(run => run.Scope, StringComparer.Ordinal)
                .ThenBy(run => run.Mode, StringComparer.Ordinal)
                .ToArray();

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
            else
                PrintText(project, rows);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CodeMeridian relationship-health report");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return 1;
        }
    }

    private static RelationshipHealthRun? ParseRun(GraphQlNode node)
    {
        var properties = node.Properties.ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal);
        if (!string.Equals(Read("externalKind"), "IndexRun", StringComparison.Ordinal))
            return null;
        if (string.Equals(Read("indexRunKind"), "IndexScopeCatalog", StringComparison.Ordinal))
            return null;

        var calls = ParseOutcomes(Read("callRelationshipOutcomes"));
        var references = ParseOutcomes(Read("referenceRelationshipOutcomes"));
        return new RelationshipHealthRun(
            Read("language") ?? "CSharp",
            Read("resolutionScope") ?? "project",
            Read("mode") ?? "full",
            ReadBool("usedFullResolutionCatalog"),
            ReadInt("scannedFileCount"),
            ReadInt("ingestedFileCount"),
            calls.Attempted + references.Attempted,
            calls.Attempted,
            references.Attempted,
            calls.ResolvedLocal + references.ResolvedLocal,
            calls.ExternalOrUnindexed + references.ExternalOrUnindexed,
            calls.UnresolvedLocal + references.UnresolvedLocal,
            calls.Indeterminate + references.Indeterminate,
            calls.DuplicateEdges + references.DuplicateEdges,
            calls.SyntheticEdges + references.SyntheticEdges,
            calls.Reasons,
            references.Reasons,
            MergeCounts(calls.FailureCountsByFileRole, references.FailureCountsByFileRole),
            DateTimeOffset.TryParse(Read("completedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var completedAt)
                ? completedAt
                : DateTimeOffset.MinValue,
            ReadInt("resolutionCatalogFileCount"),
            ReadInt("resolutionCatalogLoadDurationMs"),
            ReadLong("resolutionCatalogHeapUsedBytes"),
            Read("resolutionCatalogReason"));

        string? Read(string key) => properties.GetValueOrDefault(key);
        int ReadInt(string key) => int.TryParse(Read(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        long ReadLong(string key) => long.TryParse(Read(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        bool ReadBool(string key) => bool.TryParse(Read(key), out var value) && value;
    }

    private static RelationshipOutcomes ParseOutcomes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new RelationshipOutcomes();

        try
        {
            return JsonSerializer.Deserialize<RelationshipOutcomes>(json, JsonOptions) ?? new RelationshipOutcomes();
        }
        catch (JsonException)
        {
            return new RelationshipOutcomes();
        }
    }

    private static IReadOnlyDictionary<string, int> MergeCounts(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right) =>
        left.Concat(right)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal);

    private static void PrintText(string project, IReadOnlyList<RelationshipHealthRun> rows)
    {
        Console.WriteLine($"CodeMeridian relationship health — {project}");
        Console.WriteLine("Language | Scope | Mode | Catalog | Attempted | Resolved | External | Unresolved | Indeterminate | Duplicate | Synthetic | Scanned | Ingested");
        Console.WriteLine("--- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---:");
        foreach (var row in rows)
        {
            Console.WriteLine(
                $"{row.Language} | {row.Scope} | {row.Mode} | {(row.UsedFullResolutionCatalog ? "full" : "partial")} | "
                + $"{row.Attempted} | {row.ResolvedLocal} | {row.ExternalOrUnindexed} | {row.UnresolvedLocal} | "
                + $"{row.Indeterminate} | {row.Duplicate} | {row.Synthetic} | {row.ScannedFiles} | {row.IngestedFiles}");
            PrintReasons("calls", row.CallReasons, row.AttemptedCalls);
            PrintReasons("references", row.ReferenceReasons, row.AttemptedReferences);
            if (row.FailureCountsByFileRole.Count > 0)
                Console.WriteLine($"  file roles: {string.Join(", ", row.FailureCountsByFileRole.Select(item => $"{item.Key}={item.Value}"))}");
            if (row.ResolutionCatalogFileCount > 0)
                Console.WriteLine($"  catalog performance: files={row.ResolutionCatalogFileCount}, load={row.ResolutionCatalogLoadDurationMs}ms, heap={row.ResolutionCatalogHeapUsedBytes} bytes");
            if (row.ResolutionCatalogReason is not null)
                Console.WriteLine($"  catalog fallback: {row.ResolutionCatalogReason}");
        }
    }

    private static void PrintReasons(string label, IReadOnlyDictionary<string, int> reasons, int attempted)
    {
        if (reasons.Count == 0)
            return;

        Console.WriteLine($"  {label}: {string.Join(", ", reasons
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => attempted > 0
                ? $"{item.Key}={item.Value} ({(item.Value * 100d / attempted).ToString("0.0", CultureInfo.InvariantCulture)}%)"
                : $"{item.Key}={item.Value}"))}");
    }

    private static HttpClient CreateHttpClient(string codeMeridianUrl, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri($"{codeMeridianUrl.TrimEnd('/')}/"), Timeout = TimeSpan.FromMinutes(2) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private sealed record GraphQlEnvelope(GraphQlData? Data, IReadOnlyList<GraphQlError>? Errors);
    private sealed record GraphQlData(IReadOnlyList<GraphQlNode> Nodes);
    private sealed record GraphQlNode(string Id, IReadOnlyList<GraphQlProperty> Properties);
    private sealed record GraphQlProperty(string Key, string? Value);
    private sealed record GraphQlError(string Message);

    private sealed record RelationshipOutcomes
    {
        public int Attempted { get; init; }
        public int ResolvedLocal { get; init; }
        public int ExternalOrUnindexed { get; init; }
        public int UnresolvedLocal { get; init; }
        public int Indeterminate { get; init; }
        public int DuplicateEdges { get; init; }
        public int SyntheticEdges { get; init; }
        public IReadOnlyDictionary<string, int> Reasons { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> FailureCountsByFileRole { get; init; } = new Dictionary<string, int>();
    }

    private sealed record RelationshipHealthRun(
        string Language,
        string Scope,
        string Mode,
        bool UsedFullResolutionCatalog,
        int ScannedFiles,
        int IngestedFiles,
        int Attempted,
        int AttemptedCalls,
        int AttemptedReferences,
        int ResolvedLocal,
        int ExternalOrUnindexed,
        int UnresolvedLocal,
        int Indeterminate,
        int Duplicate,
        int Synthetic,
        IReadOnlyDictionary<string, int> CallReasons,
        IReadOnlyDictionary<string, int> ReferenceReasons,
        IReadOnlyDictionary<string, int> FailureCountsByFileRole,
        DateTimeOffset CompletedAt,
        int ResolutionCatalogFileCount,
        int ResolutionCatalogLoadDurationMs,
        long ResolutionCatalogHeapUsedBytes,
        string? ResolutionCatalogReason);
}
