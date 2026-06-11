using System.Net.Http.Headers;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ClearCommand(IToolConfigurationService configurationService)
{
    public async Task<int> RunAsync(ClearCommandOptions options)
    {
        var context = configurationService.CreateContext(path: null);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);
        var project = configurationService.ResolveProject(context, options.Project, includeFallback: false);

        if (!options.ClearAllCodeGraph && string.IsNullOrWhiteSpace(project))
        {
            Console.Error.WriteLine("error: specify --project <name> or --all-code-graph.");
            return 1;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(context.ApiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);

        var client = new CodeMeridianClient(httpClient);

        if (options.ClearAllCodeGraph)
        {
            Console.WriteLine($"Clearing all indexed code graph nodes at {codeMeridianUrl}...");
            await client.ClearCodeGraphAsync();
            Console.WriteLine("Code graph cleared. Documentation knowledge was preserved.");
            return 0;
        }

        Console.WriteLine($"Clearing project '{project}' at {codeMeridianUrl}...");
        await client.ClearProjectKnowledgeAsync(project);
        Console.WriteLine("Project knowledge cleared.");
        return 0;
    }
}
