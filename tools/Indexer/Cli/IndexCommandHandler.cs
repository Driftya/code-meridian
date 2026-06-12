using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexCommandHandler(
    IOptions<ResolvedIndexerSettings> settings,
    IProjectDiscoveryService projectDiscoveryService,
    IIndexerStoragePathService storagePathService,
    DiagnosticsCommand diagnosticsCommand)
{
    private readonly ResolvedIndexerSettings _settings = settings.Value;

    public async Task<int> RunAsync()
    {
        if (_settings.ListCapabilities)
        {
            PrintCapabilities();
            return 0;
        }

        if (!_settings.RootPath.Exists)
        {
            Console.Error.WriteLine($"error: directory not found: {_settings.RootPath.FullName}");
            return 1;
        }

        var hasCSharp = !_settings.SkipCSharp && projectDiscoveryService.ContainsFile(_settings.RootPath, ".cs");
        var typeScriptRoots = _settings.SkipTypeScript ? [] : projectDiscoveryService.FindTypeScriptRoots(_settings.RootPath);
        var hasTypeScript = typeScriptRoots.Count > 0;
        var cacheDirectory = storagePathService.ResolveCacheDirectory(_settings.RootPath, _settings.Project, _settings.StorageMode);
        var cache = IncrementalIndexCache.Load(cacheDirectory, _settings.Project);
        var indexableFiles = IndexExecutionPlanBuilder.EnumerateIndexableFiles(_settings.RootPath, hasCSharp, hasTypeScript, _settings.IncludeDocs);
        var incrementalPlan = IndexExecutionPlanBuilder.BuildPlan(cache, _settings.RootPath, indexableFiles, forceFull: _settings.Clear || !_settings.Incremental);
        var changedFiles = _settings.Incremental && !_settings.Clear ? incrementalPlan.ChangedFiles : null;
        var deletedFiles = IndexExecutionPlanBuilder.GetDeletedFiles(incrementalPlan, _settings.Incremental, _settings.Clear);

        Console.WriteLine("CodeMeridian index");
        Console.WriteLine($"  Root    : {_settings.RootPath.FullName}");
        Console.WriteLine($"  Project : {_settings.Project}");
        Console.WriteLine($"  Server  : {_settings.CodeMeridianUrl}");
        Console.WriteLine($"  Storage : {_settings.StorageMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Cache   : {cacheDirectory.FullName}");
        Console.WriteLine($"  Mode    : {(_settings.Incremental && !_settings.Clear ? "incremental" : "full")}");

        if (!hasCSharp && !hasTypeScript)
        {
            Console.WriteLine("No enabled indexers found matching this project.");
            Console.WriteLine("Use --list-capabilities to inspect available indexers.");
            return 0;
        }

        if (_settings.DryRun)
        {
            PrintDryRun(hasCSharp, typeScriptRoots, incrementalPlan, _settings.Incremental && !_settings.Clear);
            return 0;
        }

        var exitCode = 0;
        var clearNextIndexer = _settings.Clear;

        if (_settings.Incremental && !_settings.Clear && !incrementalPlan.HasChanges)
        {
            Console.WriteLine("No file changes detected since the last successful index run.");
            return 0;
        }

        if (hasCSharp)
        {
            exitCode = await RunCSharpIndexerAsync(clearNextIndexer, changedFiles, deletedFiles);
            if (exitCode != 0 || _settings.Watch)
                return exitCode;

            clearNextIndexer = false;
        }

        if (hasTypeScript)
        {
            if (changedFiles is not null || deletedFiles.Count > 0)
            {
                await new ProjectFileDeletionService(_settings.CodeMeridianUrl, _settings.ApiKey, _settings.Project, _settings.RootPath).DeleteAsync(
                    FilterTypeScriptIndexerFiles(changedFiles ?? [], typeScriptRoots, _settings.IncludeDocs && !hasCSharp)
                        .Concat(FilterTypeScriptIndexerFiles(deletedFiles, typeScriptRoots, _settings.IncludeDocs && !hasCSharp)));
            }

            var tsIndexerRoot = ResolveTypeScriptIndexerRoot();
            if (tsIndexerRoot is null)
            {
                Console.Error.WriteLine("error: TypeScript indexer assets were not found.");
                Console.Error.WriteLine("Reinstall the CodeMeridian indexer tool or run from a source checkout.");
                return 1;
            }

            exitCode = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(tsIndexerRoot);
            if (exitCode != 0)
                return exitCode;

            var tsxCommand = TypeScriptIndexerProcessRunner.ResolveTsxCommand(tsIndexerRoot);
            if (tsxCommand is null)
            {
                Console.Error.WriteLine("error: local tsx binary was not found for the TypeScript indexer.");
                Console.Error.WriteLine("Reinstall the indexer dependencies or run from a source checkout.");
                return 1;
            }

            foreach (var typeScriptRoot in typeScriptRoots)
            {
                var changedTypeScriptFiles = changedFiles is null
                    ? null
                    : FilterFilesForRoot(
                        changedFiles,
                        typeScriptRoot,
                        file => IndexExecutionPlanBuilder.IsTypeScriptSourceFile(file) || (_settings.IncludeDocs && !hasCSharp && IndexExecutionPlanBuilder.IsDocumentationFile(file)))
                    .ToArray();

                if (changedTypeScriptFiles is { Length: 0 })
                    continue;

                var filesList = changedTypeScriptFiles is null
                    ? null
                    : WriteFilesList(cacheDirectory, typeScriptRoot, changedTypeScriptFiles);

                var tsArgs = TypeScriptIndexerCommandBuilder.BuildTypeScriptIndexerArgs(tsIndexerRoot, typeScriptRoot, _settings.Project);
                TypeScriptIndexerCommandBuilder.AddTypeScriptIndexerOptions(
                    tsArgs,
                    _settings.CodeMeridianUrl,
                    _settings.Watch,
                    clearNextIndexer,
                    _settings.IncludeDocs && !hasCSharp && typeScriptRoots.Count == 1,
                    filesList);

                exitCode = await TypeScriptIndexerProcessRunner.RunAsync(tsxCommand, tsArgs, tsIndexerRoot);
                if (exitCode != 0 || _settings.Watch)
                    return exitCode;

                clearNextIndexer = false;
            }
        }

        if (_settings.IncludeDocs)
        {
            exitCode = await RunDocumentIndexerAsync(changedFiles, deletedFiles);
            if (exitCode != 0 || _settings.Watch)
                return exitCode;
        }

        if (!_settings.SkipDiagnostics)
        {
            exitCode = await diagnosticsCommand.RunAsync(
                _settings.RootPath,
                typeScriptRoots,
                _settings.Project,
                _settings.CodeMeridianUrl,
                _settings.ApiKey,
                _settings.AllowRepoScripts);
            if (exitCode != 0)
                return exitCode;
        }

        if (_settings.RebuildKeywords)
        {
            exitCode = await RebuildKeywordGraphAsync();
            if (exitCode != 0)
                return exitCode;
        }

        if (exitCode == 0)
            cache.Save(incrementalPlan);

        return exitCode;
    }

    private async Task<int> RunCSharpIndexerAsync(
        bool clear,
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
        services.AddTransient<CSharpIndexer>();
        services.AddTransient<IndexerPipeline>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IndexerPipeline>();

        await pipeline.RunAsync(_settings.RootPath, _settings.Project, clear, changedFiles, deletedFiles);

        if (!_settings.Watch)
            return 0;

        var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        var watchLoop = new IndexWatchLoop(_settings.RootPath, pipeline, logger);
        await watchLoop.RunAsync(_settings.Project, cts.Token);
        return 0;
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

        if (changedFiles is not null && documentPlan.FilesToDelete.Count > 0)
            await new ProjectFileDeletionService(_settings.CodeMeridianUrl, _settings.ApiKey, _settings.Project, _settings.RootPath).DeleteAsync(documentPlan.FilesToDelete);

        if (documentPlan.FilesToIngest.Count == 0)
            return 0;

        var logger = provider.GetRequiredService<ILogger<DocumentIndexerPipeline>>();
        logger.LogInformation("Found {Count} documentation files to ingest.", documentPlan.FilesToIngest.Count);
        await pipeline.IngestAsync(documentPlan.FilesToIngest.ToArray(), _settings.Project, _settings.RootPath.FullName, CancellationToken.None);
        return 0;
    }

    private void PrintDryRun(
        bool hasCSharp,
        IReadOnlyList<DirectoryInfo> typeScriptRoots,
        IncrementalIndexPlan incrementalPlan,
        bool incremental)
    {
        Console.WriteLine();
        Console.WriteLine("Dry run:");
        Console.WriteLine($"  Clear first       : {_settings.Clear}");
        Console.WriteLine($"  Incremental       : {(incremental ? "enabled" : "disabled")}");
        Console.WriteLine($"  Changed files     : {incrementalPlan.ChangedFiles.Count}");
        Console.WriteLine($"  Deleted files     : {incrementalPlan.DeletedFiles.Count}");
        Console.WriteLine($"  Include docs      : {_settings.IncludeDocs}");
        Console.WriteLine($"  Watch mode        : {_settings.Watch}");
        Console.WriteLine($"  Diagnostics       : {(_settings.SkipDiagnostics ? "skipped" : "enabled")}");
        Console.WriteLine($"  Rebuild keywords  : {_settings.RebuildKeywords}");
        Console.WriteLine($"  C# indexer        : {(hasCSharp ? "enabled" : "not applicable")}");
        Console.WriteLine($"  TypeScript roots  : {(typeScriptRoots.Count == 0 ? "none" : typeScriptRoots.Count)}");

        foreach (var typeScriptRoot in typeScriptRoots)
            Console.WriteLine($"    - {Path.GetRelativePath(_settings.RootPath.FullName, typeScriptRoot.FullName)}");

        Console.WriteLine($"  Project context   : {_settings.Project}");
    }

    private IEnumerable<string> FilterTypeScriptIndexerFiles(
        IEnumerable<string> relativePaths,
        IReadOnlyCollection<DirectoryInfo> typeScriptRoots,
        bool includeDocs) =>
        typeScriptRoots.SelectMany(root => FilterFilesForRoot(
            relativePaths,
            root,
            file => IndexExecutionPlanBuilder.IsTypeScriptSourceFile(file) || (includeDocs && IndexExecutionPlanBuilder.IsDocumentationFile(file))));

    private IEnumerable<string> FilterFilesForRoot(
        IEnumerable<string> relativePaths,
        DirectoryInfo targetRoot,
        Func<FileInfo, bool> predicate)
    {
        foreach (var relativePath in relativePaths)
        {
            var fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _settings.RootPath.FullName);
            if (!fullPath.StartsWith(targetRoot.FullName, StringComparison.OrdinalIgnoreCase))
                continue;

            var file = new FileInfo(fullPath);
            if (predicate(file))
                yield return fullPath;
        }
    }

    private FileInfo WriteFilesList(
        DirectoryInfo cacheDirectory,
        DirectoryInfo languageRoot,
        IReadOnlyCollection<string> fullPaths)
    {
        cacheDirectory.Create();
        var file = new FileInfo(Path.Combine(
            cacheDirectory.FullName,
            $"ts-files-{Hash($"{_settings.Project}|{languageRoot.FullName}")}.txt"));

        File.WriteAllLines(file.FullName, fullPaths.Order(StringComparer.OrdinalIgnoreCase));
        return file;
    }

    private DirectoryInfo? ResolveTypeScriptIndexerRoot()
    {
        var repositoryRoot = projectDiscoveryService.FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
            ?? projectDiscoveryService.FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));

        if (repositoryRoot is not null)
        {
            var sourceRoot = new DirectoryInfo(Path.Combine(repositoryRoot.FullName, "tools", "TsIndexer"));
            if (File.Exists(Path.Combine(sourceRoot.FullName, "src", "index.ts")))
                return sourceRoot;
        }

        var packagedRoot = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "tools", "TsIndexer"));
        return File.Exists(Path.Combine(packagedRoot.FullName, "src", "index.ts")) ? packagedRoot : null;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private void PrintCapabilities()
    {
        var tsIndexerRoot = ResolveTypeScriptIndexerRoot();

        Console.WriteLine("""
            CodeMeridian indexer capabilities

            Available:
              C# / Roslyn      yes
              Documentation    yes
            """);

        Console.WriteLine($"  TypeScript/TSX   {(tsIndexerRoot is null ? "no - assets not found" : "yes")}");
        Console.WriteLine("  Diagnostics      yes - skip with --skip-diagnostics");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  codemeridian index . --clear");
        Console.WriteLine("  codemeridian index . --keywords");
        Console.WriteLine("  codemeridian index . --storage global");
        Console.WriteLine("  codemeridian index . --storage repo");
        Console.WriteLine("  codemeridian index . --project MyProject --watch");
        Console.WriteLine("  codemeridian index . --dry-run");
        Console.WriteLine("  codemeridian init .");
        Console.WriteLine("  codemeridian init --global");
        Console.WriteLine("  codemeridian serve");
        Console.WriteLine("  codemeridian doctor --project MyProject");
        Console.WriteLine("  codemeridian check-drift --project MyProject --fail-on high");
        Console.WriteLine("  codemeridian clear --project MyProject");
        Console.WriteLine("  codemeridian clear --all-code-graph");
    }

    private async Task<int> RebuildKeywordGraphAsync()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.CodeMeridianUrl, UriKind.Absolute)
        };

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var client = new CodeMeridianClient(httpClient);

        Console.WriteLine("Rebuilding keyword graph...");

        try
        {
            await client.RebuildKeywordGraphAsync(_settings.Project);
            Console.WriteLine($"Keyword graph rebuilt for '{_settings.Project}'.");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"error: keyword graph rebuild failed: {ex.Message}");
            return 1;
        }
    }
}
