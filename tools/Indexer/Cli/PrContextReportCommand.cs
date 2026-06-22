using System.Net.Http.Headers;
using System.Text.Json;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class PrContextReportCommand(
    IToolConfigurationService configurationService,
    IPrContextGitDiffProvider gitDiffProvider)
{
    private readonly Func<string, string?, HttpClient> _httpClientFactory = CreateHttpClient;

    internal PrContextReportCommand(
        IToolConfigurationService configurationService,
        IPrContextGitDiffProvider gitDiffProvider,
        Func<string, string?, HttpClient> httpClientFactory)
        : this(configurationService, gitDiffProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<int> RunAsync(PrContextReportCommandOptions options)
    {
        var context = configurationService.CreateContext(options.Path);
        var project = configurationService.ResolveProject(context, options.Project);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);

        IReadOnlyCollection<string> changedFiles;
        try
        {
            changedFiles = await gitDiffProvider.GetChangedFilesAsync(
                context.RootPath,
                options.BaseRef,
                options.HeadRef,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        using var httpClient = _httpClientFactory(codeMeridianUrl, context.ApiKey);
        var client = new CodeMeridianClient(httpClient);

        PrContextReportResponse? report;
        try
        {
            report = await client.BuildPrContextReportAsync(new PrContextReportRequest(
                changedFiles,
                project,
                options.BaseRef,
                options.HeadRef,
                options.IncludeDocs));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        if (report is null)
        {
            Console.Error.WriteLine("error: backend returned an empty or non-success PR context report response.");
            return 1;
        }

        var rendered = options.Format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? RenderJson(report)
            : RenderMarkdown(report);

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            Console.WriteLine(rendered);
            return 0;
        }

        var outputPath = Path.GetFullPath(options.OutputPath, context.RootPath.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, rendered);
        Console.WriteLine(outputPath);
        return 0;
    }

    private static string RenderJson(PrContextReportResponse report)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(report, options);
    }

    private static string RenderMarkdown(PrContextReportResponse report)
    {
        var writer = new StringWriter();

        writer.WriteLine("# PR Context Report");
        writer.WriteLine();
        writer.WriteLine($"Project: `{report.ProjectContext ?? "unknown"}`");
        writer.WriteLine($"Base: `{report.BaseRef ?? "unknown"}`");
        writer.WriteLine($"Head: `{report.HeadRef ?? "unknown"}`");
        writer.WriteLine($"Changed files: **{report.ChangedFiles.Count}**");
        writer.WriteLine($"Changed nodes: **{report.ChangedNodes.Count}**");
        writer.WriteLine();

        writer.WriteLine("## Changed Files");
        foreach (var file in report.ChangedFiles)
            writer.WriteLine($"- `{file}`");
        writer.WriteLine();

        writer.WriteLine("## Changed Nodes");
        if (report.ChangedNodes.Count == 0)
        {
            writer.WriteLine("- No indexed graph nodes matched the changed files.");
        }
        else
        {
            foreach (var node in report.ChangedNodes.Take(12))
                writer.WriteLine($"- `{node.Type}` `{node.Name}` - `{node.FilePath}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Impact Radius");
        if (report.ImpactedNodes.Count == 0)
        {
            writer.WriteLine("- No downstream callers or dependents were matched.");
        }
        else
        {
            foreach (var item in report.ImpactedNodes)
                writer.WriteLine($"- `{item.Node.Name}` - `{item.Node.FilePath}` (distance {item.Distance}, matched from {item.ChangedNodeMatches} changed node(s))");
        }
        writer.WriteLine();

        writer.WriteLine("## Missing Tests");
        if (report.MissingTestNodes.Count == 0)
        {
            writer.WriteLine("- No obvious unshielded changed nodes were found.");
        }
        else
        {
            foreach (var node in report.MissingTestNodes)
                writer.WriteLine($"- `{node.Name}` - `{node.FilePath}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Hotspot Warnings");
        if (report.HotspotWarnings.Count == 0)
        {
            writer.WriteLine("- No hotspot or high-churn warnings were triggered.");
        }
        else
        {
            foreach (var warning in report.HotspotWarnings)
                writer.WriteLine($"- `{warning.Node.Name}` - {warning.Reason}");
        }
        writer.WriteLine();

        writer.WriteLine("## Related Documentation");
        if (report.RelatedDocuments.Count == 0)
        {
            writer.WriteLine("- No related documentation cleared the keyword-confidence threshold.");
        }
        else
        {
            foreach (var document in report.RelatedDocuments)
                writer.WriteLine($"- `{document.Source}` - {document.Confidence} confidence; matched {string.Join(", ", document.MatchedKeywords.Select(keyword => $"`{keyword}`"))}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review Focus");
        foreach (var item in report.ReviewFocus)
            writer.WriteLine($"- {item}");

        return writer.ToString().TrimEnd();
    }

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
