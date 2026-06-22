using System.Net.Http.Headers;
using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Versioning;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class StatusCommand
{
    private readonly Func<string, string?, HttpClient> _httpClientFactory;

    public StatusCommand()
        : this(CreateHttpClient)
    {
    }

    internal StatusCommand(Func<string, string?, HttpClient> httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<int> RunDoctorAsync(
        string? project,
        string codeMeridianUrl,
        string? apiKey)
    {
        var status = await FetchStatusAsync("CodeMeridian doctor", project, codeMeridianUrl, apiKey);
        if (status is null)
            return 1;

        Console.WriteLine("CodeMeridian doctor");
        Console.WriteLine("  Server reachable        : yes");
        Console.WriteLine("  MCP endpoint reachable   : yes");
        Console.WriteLine($"  Neo4j reachable         : {(status.Neo4jReachable ? "yes" : "no")}");
        Console.WriteLine($"  Indexed nodes           : {status.IndexedNodes:N0}");
        Console.WriteLine($"  Call edges              : {status.CallEdges:N0}");
        Console.WriteLine($"  Docs indexed            : {status.DocumentsIndexed:N0}");
        Console.WriteLine($"  Diagnostics indexed     : {status.DiagnosticsIndexed:N0}");
        Console.WriteLine($"  Graph drift             : {status.GraphDrift}");
        Console.WriteLine($"  Embeddings              : {(status.EmbeddingsEnabled ? $"{status.EmbeddingProvider} ({status.EmbeddingDimensions} dims)" : "disabled")}");

        if (!string.IsNullOrWhiteSpace(status.Error))
            Console.WriteLine($"  Note                    : {status.Error}");

        return status.Neo4jReachable ? 0 : 1;
    }

    public async Task<int> RunDriftVerificationAsync(
        string? project,
        string codeMeridianUrl,
        string? apiKey,
        string failOn)
    {
        var status = await FetchStatusAsync("CodeMeridian drift verification", project, codeMeridianUrl, apiKey);
        if (status is null)
            return 1;

        Console.WriteLine("CodeMeridian drift verification");
        Console.WriteLine("  Server reachable       : yes");
        Console.WriteLine("  MCP endpoint reachable : yes");
        Console.WriteLine($"  Neo4j reachable        : {(status.Neo4jReachable ? "yes" : "no")}");
        Console.WriteLine($"  Fail threshold         : {failOn}");
        Console.WriteLine($"  Graph drift            : {status.GraphDrift}");
        Console.WriteLine();
        Console.WriteLine(status.GraphDriftReport);

        if (!status.Neo4jReachable)
            return 1;

        return SeverityAtLeast(status.GraphDrift, failOn) ? 2 : 0;
    }

    public async Task<int> RunReportAsync(
        string? project,
        string codeMeridianUrl,
        string? apiKey)
    {
        using var httpClient = _httpClientFactory(codeMeridianUrl, apiKey);
        var client = new CodeMeridianClient(httpClient);

        try
        {
            var report = await client.GetArchitectureReportAsync(project);
            if (string.IsNullOrWhiteSpace(report))
            {
                PrintUnreachable("CodeMeridian report", "backend returned an empty or non-success response.");
                return 1;
            }

            Console.WriteLine(report.TrimEnd());
            return 0;
        }
        catch (Exception ex)
        {
            PrintUnreachable("CodeMeridian report", ex.Message);
            return 1;
        }
    }

    public async Task<int> RunTraceEndpointAsync(
        string route,
        string? project,
        string codeMeridianUrl,
        string? apiKey,
        string detailLevel)
    {
        using var httpClient = _httpClientFactory(codeMeridianUrl, apiKey);
        var client = new CodeMeridianClient(httpClient);

        try
        {
            var report = await client.GetEndpointTraceAsync(route, project, detailLevel);
            if (string.IsNullOrWhiteSpace(report))
            {
                PrintUnreachable("CodeMeridian trace-endpoint", "backend returned an empty or non-success response.");
                return 1;
            }

            Console.WriteLine(report.TrimEnd());
            return 0;
        }
        catch (Exception ex)
        {
            PrintUnreachable("CodeMeridian trace-endpoint", ex.Message);
            return 1;
        }
    }

    public async Task<int> RunVersionAsync(string codeMeridianUrl, string? apiKey)
    {
        var toolVersion = CodeMeridianVersionReader.ReadFrom(typeof(StatusCommand).Assembly, "CodeMeridian.Indexer");

        Console.WriteLine("CodeMeridian version");
        Console.WriteLine($"  Client tool version    : {toolVersion.ProductVersion}");
        Console.WriteLine($"  Graph contract version : {toolVersion.GraphContractVersion}");
        Console.WriteLine($"  Cache version          : {toolVersion.CacheVersion}");

        try
        {
            using var httpClient = _httpClientFactory(codeMeridianUrl, apiKey);
            var client = new CodeMeridianClient(httpClient);
            var serverVersion = await client.GetVersionAsync();

            if (serverVersion is null)
            {
                Console.WriteLine("  MCP server version     : fetch failed");
                return 0;
            }

            Console.WriteLine($"  MCP server version     : {serverVersion.ProductVersion}");
            Console.WriteLine($"  MCP graph contract     : {serverVersion.GraphContractVersion}");
            Console.WriteLine($"  MCP cache version      : {serverVersion.CacheVersion}");
            return 0;
        }
        catch
        {
            Console.WriteLine("  MCP server version     : fetch failed");
            return 0;
        }
    }

    private static async Task<DoctorStatusResponse?> FetchStatusAsync(
        string title,
        string? project,
        string codeMeridianUrl,
        string? apiKey)
    {
        using var httpClient = CreateHttpClient(codeMeridianUrl, apiKey);

        var client = new CodeMeridianClient(httpClient);

        try
        {
            var status = await client.GetDoctorStatusAsync(project);
            if (status is not null)
                return status;
        }
        catch (Exception ex)
        {
            PrintUnreachable(title, ex.Message);
            return null;
        }

        PrintUnreachable(title, "backend returned a non-success response.");
        return null;
    }

    private static void PrintUnreachable(string title, string message)
    {
        Console.Error.WriteLine(title);
        Console.Error.WriteLine("  Server reachable        : no");
        Console.Error.WriteLine("  MCP endpoint reachable   : no");
        Console.Error.WriteLine("  Neo4j reachable          : no");
        Console.Error.WriteLine($"  Error                    : {message}");
    }

    private static bool SeverityAtLeast(string actual, string threshold)
    {
        var actualRank = SeverityRank(actual);
        var thresholdRank = SeverityRank(threshold);
        return actualRank >= thresholdRank;
    }

    private static int SeverityRank(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "low" => 1,
            "moderate" => 2,
            "high" => 3,
            _ => 0
        };

    private static HttpClient CreateHttpClient(string codeMeridianUrl, string? apiKey)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return httpClient;
    }
}
