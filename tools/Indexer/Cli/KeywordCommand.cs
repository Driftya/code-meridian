using System.Net.Http.Headers;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class KeywordCommand(IToolConfigurationService configurationService)
{
    public async Task<int> RunAsync(KeywordCommandOptions options)
    {
        var context = configurationService.CreateContext(options.Path);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);
        var project = configurationService.ResolveProject(context, options.Project, includeFallback: false);

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(context.ApiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);

        var client = new CodeMeridianClient(httpClient);

        try
        {
            if (options.Action == KeywordCommandAction.Classify)
            {
                Console.WriteLine($"Classifying keywords at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
                await client.ClassifyKeywordsAsync(project);
                Console.WriteLine(string.IsNullOrWhiteSpace(project)
                    ? "Keywords classified for all indexed projects."
                    : $"Keywords classified for '{project}'.");
                return 0;
            }

            Console.WriteLine($"Rebuilding keyword graph at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
            await client.RebuildKeywordGraphAsync(project);
            Console.WriteLine(string.IsNullOrWhiteSpace(project)
                ? "Keyword graph rebuilt for all indexed projects."
                : $"Keyword graph rebuilt for '{project}'.");
            return 0;
        }
        catch (Exception ex)
        {
            var operation = options.Action == KeywordCommandAction.Classify
                ? "keyword classification"
                : "keyword graph rebuild";
            Console.Error.WriteLine($"error: {operation} failed: {ex.Message}");
            return 1;
        }
    }
}
