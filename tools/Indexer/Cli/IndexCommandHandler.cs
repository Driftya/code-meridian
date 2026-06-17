using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using CodeMeridian.Tooling.Watching;
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

        var context = BuildExecutionContext(_settings.Clear);

        Console.WriteLine("CodeMeridian index");
        Console.WriteLine($"  Root    : {_settings.RootPath.FullName}");
        Console.WriteLine($"  Project : {_settings.Project}");
        Console.WriteLine($"  Server  : {_settings.CodeMeridianUrl}");
        Console.WriteLine($"  Storage : {_settings.StorageMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Cache   : {context.CacheDirectory.FullName}");
        Console.WriteLine($"  Mode    : {(_settings.Incremental && !_settings.Clear ? "incremental" : "full")}");
        if (_settings.HasOutdatedLocalConfig)
        {
            Console.WriteLine();
            Console.WriteLine($"warning: meridian.json is using config version {_settings.LocalConfigVersion}.");
            Console.WriteLine($"warning: run `codemeridian init .` to merge version {_settings.CurrentConfigVersion} defaults into the existing file.");
        }

        if (!context.HasCSharp && !context.HasTypeScript && !context.HasConfiguration)
        {
            Console.WriteLine("No enabled indexers found matching this project.");
            Console.WriteLine("Use --list-capabilities to inspect available indexers.");
            return 0;
        }

        if (_settings.DryRun)
        {
            PrintDryRun(context.HasCSharp, context.TypeScriptRoots, context.IncrementalPlan, _settings.Incremental && !_settings.Clear);
            return 0;
        }

        if (_settings.Incremental && !_settings.Clear && !context.IncrementalPlan.HasChanges)
        {
            Console.WriteLine("No file changes detected since the last successful index run.");
            return 0;
        }

        var exitCode = await RunIndexPassAsync(
            context,
            clear: _settings.Clear,
            changedFiles: context.ChangedFiles,
            deletedFiles: context.DeletedFiles);

        if (exitCode != 0)
            return exitCode;

        context.Cache.Save(context.IncrementalPlan);

        if (!_settings.Watch)
            return 0;

        var services = BuildLoggingServices();
        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<IndexCommandHandler>>();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var watchLoop = new IndexWatchLoop(_settings.RootPath, logger);
        await watchLoop.RunAsync(async (_, _) =>
        {
            var watchContext = BuildExecutionContext(clear: false);
            if (!watchContext.IncrementalPlan.HasChanges)
                return;

            var watchExitCode = await RunIndexPassAsync(
                watchContext,
                clear: false,
                changedFiles: watchContext.ChangedFiles,
                deletedFiles: watchContext.DeletedFiles);

            if (watchExitCode == 0)
                watchContext.Cache.Save(watchContext.IncrementalPlan);
        }, cts.Token);

        return 0;
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
        services.AddSingleton(IndexedFileRoleClassifierFactory.Create(_settings.FileRoles));
        services.AddTransient<CSharpIndexer>();
        services.AddTransient<IndexerPipeline>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IndexerPipeline>();

        await pipeline.RunAsync(_settings.RootPath, _settings.Project, clear, changedFiles, deletedFiles);
        return 0;
    }

    private async Task<int> RunIndexPassAsync(
        IndexExecutionContext context,
        bool clear,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
    {
        var exitCode = 0;
        var clearNextIndexer = clear;

        if (context.HasCSharp)
        {
            exitCode = await RunCSharpIndexerAsync(clearNextIndexer, changedFiles, deletedFiles);
            if (exitCode != 0)
                return exitCode;

            clearNextIndexer = false;
        }

        if (context.HasTypeScript)
        {
            exitCode = await RunTypeScriptIndexerAsync(context, changedFiles, deletedFiles);
            if (exitCode != 0)
                return exitCode;

            clearNextIndexer = false;
        }

        if (_settings.IncludeDocs)
        {
            exitCode = await RunDocumentIndexerAsync(changedFiles, deletedFiles);
            if (exitCode != 0)
                return exitCode;
        }

        if (context.HasConfiguration)
        {
            exitCode = await RunConfigurationIndexerAsync(clear, changedFiles, deletedFiles);
            if (exitCode != 0)
                return exitCode;
        }

        if (!_settings.SkipDiagnostics)
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

        if (_settings.RebuildKeywords)
            return await RebuildKeywordGraphAsync();

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
        Console.WriteLine($"  Config indexer    : {(_settings.SkipConfiguration ? "skipped" : "enabled")}");

        foreach (var typeScriptRoot in typeScriptRoots)
            Console.WriteLine($"    - {Path.GetRelativePath(_settings.RootPath.FullName, typeScriptRoot.FullName)}");

        Console.WriteLine($"  Project context   : {_settings.Project}");
    }

    private IndexExecutionContext BuildExecutionContext(bool clear)
    {
        var hasCSharp = !_settings.SkipCSharp && projectDiscoveryService.ContainsFile(_settings.RootPath, ".cs");
        var typeScriptRoots = _settings.SkipTypeScript ? [] : projectDiscoveryService.FindTypeScriptRoots(_settings.RootPath);
        var hasTypeScript = typeScriptRoots.Count > 0;
        var configurationFilePatterns = _settings.ConfigurationFiles;
        var hasConfiguration = !_settings.SkipConfiguration &&
                               _settings.RootPath.EnumerateFiles("*.*", SearchOption.AllDirectories)
                                   .Where(file => !IndexExecutionPlanBuilder.IsIgnoredPath(_settings.RootPath, file))
                                   .Any(file => ConfigurationFilePatternMatcher.IsConfigurationFile(file, configurationFilePatterns));

        var cacheDirectory = storagePathService.ResolveCacheDirectory(_settings.RootPath, _settings.Project, _settings.StorageMode);
        var cache = IncrementalIndexCache.Load(cacheDirectory, _settings.Project);
        var indexableFiles = IndexExecutionPlanBuilder.EnumerateIndexableFiles(_settings.RootPath, hasCSharp, hasTypeScript, _settings.IncludeDocs, hasConfiguration);
        var incrementalPlan = IndexExecutionPlanBuilder.BuildPlan(cache, _settings.RootPath, indexableFiles, forceFull: clear || !_settings.Incremental);
        var changedFiles = _settings.Incremental && !clear ? incrementalPlan.ChangedFiles : null;
        var deletedFiles = IndexExecutionPlanBuilder.GetDeletedFiles(incrementalPlan, _settings.Incremental, clear);

        return new IndexExecutionContext(
            hasCSharp,
            typeScriptRoots,
            hasTypeScript,
            hasConfiguration,
            cacheDirectory,
            cache,
            incrementalPlan,
            changedFiles,
            deletedFiles);
    }

    private async Task<int> RunTypeScriptIndexerAsync(
        IndexExecutionContext context,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
    {
        if (changedFiles is not null || deletedFiles.Count > 0)
        {
            await new ProjectFileDeletionService(_settings.CodeMeridianUrl, _settings.ApiKey, _settings.Project, _settings.RootPath).DeleteAsync(
                context.TypeScriptRoots.SelectMany(root => FilterTypeScriptFiles(changedFiles ?? [], root))
                    .Concat(context.TypeScriptRoots.SelectMany(root => FilterTypeScriptFiles(deletedFiles, root))));
        }

        var tsIndexerRoot = ResolveTypeScriptIndexerRoot();
        if (tsIndexerRoot is null)
        {
            Console.Error.WriteLine("error: TypeScript indexer assets were not found.");
            Console.Error.WriteLine("Reinstall the CodeMeridian indexer tool or run from a source checkout.");
            return 1;
        }

        var exitCode = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(tsIndexerRoot);
        if (exitCode != 0)
            return exitCode;

        var tsxCommand = TypeScriptIndexerProcessRunner.ResolveTsxCommand(tsIndexerRoot);
        if (tsxCommand is null)
        {
            Console.Error.WriteLine("error: local tsx binary was not found for the TypeScript indexer.");
            Console.Error.WriteLine("Reinstall the indexer dependencies or run from a source checkout.");
            return 1;
        }

        var fileRoleClassifier = IndexedFileRoleClassifierFactory.Create(_settings.FileRoles);

        foreach (var typeScriptRoot in context.TypeScriptRoots)
        {
            var files = SelectTypeScriptFilesForRoot(changedFiles, typeScriptRoot).ToArray();
            if (files.Length == 0)
                continue;

            var batchFile = WriteTypeScriptBatchFile(context.CacheDirectory, typeScriptRoot, files, fileRoleClassifier);
            var tsArgs = TypeScriptIndexerCommandBuilder.BuildTypeScriptIndexerArgs(tsIndexerRoot, typeScriptRoot, _settings.Project);
            TypeScriptIndexerCommandBuilder.AddTypeScriptIndexerOptions(tsArgs, _settings.CodeMeridianUrl, batchFile);

            exitCode = await TypeScriptIndexerProcessRunner.RunAsync(
                tsxCommand,
                tsArgs,
                tsIndexerRoot,
                CreateTypeScriptIndexerEnvironment());
            if (exitCode != 0)
                return exitCode;
        }

        return 0;
    }

    private IEnumerable<string> FilterTypeScriptFiles(
        IEnumerable<string> relativePaths,
        DirectoryInfo typeScriptRoot) =>
        FilterFilesForRoot(relativePaths, typeScriptRoot, IndexExecutionPlanBuilder.IsTypeScriptSourceFile);

    private IEnumerable<string> SelectTypeScriptFilesForRoot(
        IReadOnlyCollection<string>? changedFiles,
        DirectoryInfo typeScriptRoot)
    {
        if (changedFiles is null)
        {
            return typeScriptRoot
                .EnumerateFiles("*.*", SearchOption.AllDirectories)
                .Where(file => !IndexExecutionPlanBuilder.IsIgnoredPath(_settings.RootPath, file))
                .Where(IndexExecutionPlanBuilder.IsTypeScriptSourceFile)
                .Select(file => file.FullName);
        }

        return FilterTypeScriptFiles(changedFiles, typeScriptRoot);
    }

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

    private FileInfo WriteTypeScriptBatchFile(
        DirectoryInfo cacheDirectory,
        DirectoryInfo languageRoot,
        IReadOnlyCollection<string> fullPaths,
        Application.Services.IIndexedFileRoleClassifier fileRoleClassifier)
    {
        cacheDirectory.Create();
        var file = new FileInfo(Path.Combine(
            cacheDirectory.FullName,
            $"ts-batch-{Hash($"{_settings.Project}|{languageRoot.FullName}")}.json"));

        var payload = fullPaths
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(fullPath =>
            {
                var relativeToSolutionRoot = Path.GetRelativePath(_settings.RootPath.FullName, fullPath).Replace('\\', '/');
                var relativeToLanguageRoot = Path.GetRelativePath(languageRoot.FullName, fullPath).Replace('\\', '/');
                return new TypeScriptBatchEntry(relativeToLanguageRoot, fileRoleClassifier.Classify(relativeToSolutionRoot).ToString());
            })
            .ToArray();

        File.WriteAllText(file.FullName, System.Text.Json.JsonSerializer.Serialize(payload));
        return file;
    }

    private IReadOnlyDictionary<string, string?> CreateTypeScriptIndexerEnvironment() =>
        string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["CodeMeridian_Auth_ApiKey"] = _settings.ApiKey };

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
        Console.WriteLine("  codemeridian index .");
        Console.WriteLine("  codemeridian index . --skip-keywords");
        Console.WriteLine("  codemeridian index . --storage global");
        Console.WriteLine("  codemeridian index . --storage repo");
        Console.WriteLine("  codemeridian index . --project MyProject --watch");
        Console.WriteLine("  codemeridian index . --dry-run");
        Console.WriteLine("  codemeridian init .");
        Console.WriteLine("  codemeridian init --global");
        Console.WriteLine("  codemeridian serve");
        Console.WriteLine("  codemeridian doctor --project MyProject");
        Console.WriteLine("  codemeridian report --project MyProject");
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

        Console.WriteLine("Starting keyword rebuild and classify job...");

        try
        {
            var job = await client.StartRebuildKeywordGraphAsync(_settings.Project);
            if (job is null)
            {
                Console.Error.WriteLine("error: keyword graph rebuild job could not be started.");
                return 1;
            }

            Console.WriteLine(job.Accepted
                ? $"Keyword rebuild and classify job started: {job.Job.JobId:D}"
                : $"Keyword rebuild and classify job busy: {job.Message}");
            return job.Accepted ? 0 : 2;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"error: keyword graph rebuild failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunConfigurationIndexerAsync(
        bool clear,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
    {
        var indexer = new ConfigurationIndexer();
        return await indexer.RunAsync(
            _settings.RootPath,
            _settings.Project,
            _settings.CodeMeridianUrl,
            _settings.ApiKey,
            IndexedFileRoleClassifierFactory.Create(_settings.FileRoles),
            _settings.ConfigurationFiles,
            _settings.ArchitecturePath,
            clearExistingConfiguration: clear || !_settings.Incremental,
            changedFiles: clear || !_settings.Incremental ? null : changedFiles,
            deletedFiles: clear || !_settings.Incremental ? [] : deletedFiles);
    }

    private ServiceCollection BuildLoggingServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddConsole()
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
            .AddFilter("Microsoft", LogLevel.Warning)
            .SetMinimumLevel(LogLevel.Information));
        return services;
    }

    private sealed record IndexExecutionContext(
        bool HasCSharp,
        IReadOnlyList<DirectoryInfo> TypeScriptRoots,
        bool HasTypeScript,
        bool HasConfiguration,
        DirectoryInfo CacheDirectory,
        IncrementalIndexCache Cache,
        IncrementalIndexPlan IncrementalPlan,
        IReadOnlyCollection<string>? ChangedFiles,
        IReadOnlyCollection<string> DeletedFiles);

    private sealed record TypeScriptBatchEntry(string Path, string FileRole);
}
