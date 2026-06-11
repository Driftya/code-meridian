using System.Net.Http.Headers;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class StatusCommand
{
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

    private static async Task<DoctorStatusResponse?> FetchStatusAsync(
        string title,
        string? project,
        string codeMeridianUrl,
        string? apiKey)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
}
