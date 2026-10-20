using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed partial class IndexCommandHandler
{
    private async Task<int> RunIndexPassAsync(
        IndexExecutionContext context,
        bool clear,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
        => await _profileReporter.RunAsync(_settings,
            async report =>
            {
                var exitCode = await RunIndexPassCoreAsync(context, clear, changedFiles, deletedFiles, report);
                if (exitCode == 0)
                    context.Cache.Save(context.IncrementalPlan, _settings.CodeMeridianUrl, report?.Generation, report?.ObservedAt);
                return exitCode;
            });

    private async Task<int> RunIndexPassCoreAsync(
        IndexExecutionContext context, bool clear, IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles, IndexProfileRun? report)
    {
        var exitCode = 0;
        var clearNextIndexer = clear;

        if (SqlIndexRunCoordinator.IsEnabled(_settings))
        {
            // Full reconciliation must also bypass the SQL worker's separate fingerprint cache.
            exitCode = await RunAnalyzer("sql", ["."], () => SqlIndexRunCoordinator.RunAsync(_settings, context.CacheDirectory,
                clear || !_settings.Incremental || changedFiles is null));
            if (exitCode != 0) return exitCode;
            // SQL publication owns its deletions, including edges between shared objects.
            deletedFiles = deletedFiles.Where(path => !path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        if (context.HasCSharp)
        {
            exitCode = await RunAnalyzer("csharp", ["."], () => RunCSharpIndexerAsync(clearNextIndexer, changedFiles, deletedFiles));
            if (exitCode != 0)
                return exitCode;

            clearNextIndexer = false;
        }

        if (context.HasTypeScript)
        {
            exitCode = await RunAnalyzer("typescript", Scopes(context.TypeScriptRoots), () => RunTypeScriptIndexerAsync(context, changedFiles, deletedFiles));
            if (exitCode != 0)
                return exitCode;

            clearNextIndexer = false;
        }

        if (context.HasHtmlCss)
        {
            exitCode = await RunAnalyzer("frontend", Scopes(context.HtmlCssRoots), () => RunHtmlCssIndexerAsync(context, changedFiles, deletedFiles));
            if (exitCode != 0)
                return exitCode;

            clearNextIndexer = false;
        }

        if (IncludesDocs)
        {
            exitCode = report is null ? await RunDocumentIndexerAsync(changedFiles, deletedFiles)
                : await report.RunAnalyzerAsync("documents", ["."], () => RunDocumentIndexerAsync(changedFiles, deletedFiles),
                    report.CoversAllDocuments(changedFiles), verifiedCoverage: true);
            if (exitCode != 0)
                return exitCode;
        }

        if (context.HasConfiguration)
        {
            exitCode = await RunAnalyzer("configuration", ["."], () => RunConfigurationIndexerAsync(clear, changedFiles, deletedFiles));
            if (exitCode != 0)
                return exitCode;
        }

        if (!_settings.SkipDiagnostics && !_settings.ExternalOnly)
        {
            exitCode = await diagnosticsCommand.RunAsync(
                _settings.RootPath,
                context.TypeScriptRoots,
                _settings.Project,
                _settings.CodeMeridianUrl,
                _settings.ApiKey,
                _settings.AllowRepoScripts);
            if (exitCode != 0)
                return exitCode;
        }

        if (!_settings.SkipTypeScript && !_settings.ExternalOnly)
        {
            await typeScriptScopeCatalogWriter.WriteAsync(
                _settings.Project,
                _settings.CodeMeridianUrl,
                _settings.ApiKey,
                context.TypeScriptRoots);
        }

        if (_settings.RebuildKeywords && !_settings.ExternalOnly)
            return await RebuildKeywordGraphAsync();

        return 0;

        Task<int> RunAnalyzer(string analyzer, IReadOnlyList<string> scopes, Func<Task<int>> run) => report is null
            ? run() : report.RunAnalyzerAsync(analyzer, scopes, run, changedFiles is null);

        string[] Scopes(IReadOnlyList<DirectoryInfo> roots) => roots.Select(root =>
        {
            var relative = Path.GetRelativePath(_settings.RootPath.FullName, root.FullName).Replace('\\', '/');
            return string.IsNullOrEmpty(relative) ? "." : relative;
        }).ToArray();
    }

    private async Task<int> RunDocumentIndexerAsync(
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddConsole()
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
            .AddFilter("Microsoft", LogLevel.Warning)
            .SetMinimumLevel(LogLevel.Information));

        services.AddCodeMeridianClient(_settings.CodeMeridianUrl, _settings.ApiKey);
        services.AddTransient<DocumentIndexerPipeline>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<DocumentIndexerPipeline>();

        var documentPlan = DocumentIndexRunCoordinator.BuildPlan(_settings.RootPath, changedFiles, deletedFiles);

        if (documentPlan.FilesToDelete.Count > 0)
            await new ProjectFileDeletionService(_settings.CodeMeridianUrl, _settings.ApiKey, _settings.Project, _settings.RootPath).DeleteAsync(documentPlan.FilesToDelete);

        if (documentPlan.FilesToIngest.Count == 0)
            return 0;

        var logger = provider.GetRequiredService<ILogger<DocumentIndexerPipeline>>();
        logger.LogInformation("Found {Count} documentation files to ingest.", documentPlan.FilesToIngest.Count);
        var stats = await pipeline.IngestAsync(documentPlan.FilesToIngest.ToArray(), _settings.Project, _settings.RootPath.FullName, CancellationToken.None);
        return stats.FailedFiles == 0 ? 0 : 2;
    }
}
