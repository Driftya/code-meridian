using System.Net.Http.Headers;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli;

internal static class ClearCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IndexerConfig? config)
    {
        string? project = null;
        var clearAllCodeGraph = false;
        var codeMeridianUrl = Environment.GetEnvironmentVariable("CodeMeridian_Url")
            ?? config?.CodeMeridianUrl
            ?? "http://localhost:5100";
        var apiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");

        for (var i = 0; i < rawArgs.Count; i++)
        {
            switch (rawArgs[i])
            {
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                case "--project" when i + 1 < rawArgs.Count:
                    project = rawArgs[++i];
                    break;
                case "--CodeMeridian" when i + 1 < rawArgs.Count:
                case "--url" when i + 1 < rawArgs.Count:
                    codeMeridianUrl = rawArgs[++i];
                    break;
                case "--all-code-graph":
                    clearAllCodeGraph = true;
                    break;
                default:
                    Console.Error.WriteLine($"warn: unknown option ignored: {rawArgs[i]}");
                    break;
            }
        }

        if (!clearAllCodeGraph && string.IsNullOrWhiteSpace(project))
        {
            Console.Error.WriteLine("error: specify --project <name> or --all-code-graph.");
            PrintUsage();
            return 1;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var client = new CodeMeridianClient(httpClient);

        if (clearAllCodeGraph)
        {
            Console.WriteLine($"Clearing all indexed code graph nodes at {codeMeridianUrl}...");
            await client.ClearCodeGraphAsync();
            Console.WriteLine("Code graph cleared. Documentation knowledge was preserved.");
            return 0;
        }

        Console.WriteLine($"Clearing project '{project}' at {codeMeridianUrl}...");
        await client.ClearProjectKnowledgeAsync(project!);
        Console.WriteLine("Project knowledge cleared.");
        return 0;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            CodeMeridian Clear - remove indexed knowledge from Neo4j.

            USAGE:
              codemeridian clear --project <name> [--url <url>]
              codemeridian clear --all-code-graph [--url <url>]

            SOURCE USAGE:
              dotnet run --project tools/Indexer -- clear --project <name>
              dotnet run --project tools/Indexer -- clear --all-code-graph

            OPTIONS:
              --project <name>       Remove code graph nodes and documents for one project.
              --all-code-graph       Remove all CodeNode graph data for every project. Documents are preserved.
              --url <url>            CodeMeridian server URL. Alias for --CodeMeridian.
              --CodeMeridian <url>   CodeMeridian server URL.
              -h, --help             Show this help.
            """);
    }
}
