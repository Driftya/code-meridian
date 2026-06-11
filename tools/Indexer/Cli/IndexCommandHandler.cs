using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexCommandHandler(
    IOptions<ResolvedIndexerSettings> settings,
    IProjectDiscoveryService projectDiscoveryService,
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
        var cache = IncrementalIndexCache.Load(_settings.RootPath, _settings.Project);
        var indexableFiles = EnumerateIndexableFiles(_settings.RootPath, hasCSharp, hasTypeScript, _settings.IncludeDocs).ToArray();
        var incrementalPlan = cache.BuildPlan(_settings.RootPath, indexableFiles, forceFull: _settings.Clear || !_settings.Incremental);
        var changedFiles = _settings.Incremental && !_settings.Clear ? incrementalPlan.ChangedFiles : null;
        var deletedFiles = _settings.Incremental && !_settings.Clear ? incrementalPlan.DeletedFiles : [];

        Console.WriteLine("CodeMeridian index");
        Console.WriteLine($"  Root    : {_settings.RootPath.FullName}");
        Console.WriteLine($"  Project : {_settings.Project}");
        Console.WriteLine($"  Server  : {_settings.CodeMeridianUrl}");
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
                await DeleteProjectFilesAsync(
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

            exitCode = await EnsureTypeScriptIndexerDependenciesAsync(tsIndexerRoot);
            if (exitCode != 0)
                return exitCode;

            var tsxCommand = ResolveTsxCommand(tsIndexerRoot);
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
                        file => IsTypeScriptSourceFile(file) || (_settings.IncludeDocs && !hasCSharp && IsDocumentationFile(file)))
                    .ToArray();

                if (changedTypeScriptFiles is { Length: 0 })
                    continue;

                var filesList = changedTypeScriptFiles is null
                    ? null
                    : WriteFilesList(typeScriptRoot, changedTypeScriptFiles);

                var tsArgs = BuildTypeScriptIndexerArgs(tsIndexerRoot, typeScriptRoot);
                AddTypeScriptIndexerOptions(
                    tsArgs,
                    clearNextIndexer,
                    _settings.IncludeDocs && !hasCSharp && typeScriptRoots.Count == 1,
                    filesList);

                exitCode = await RunProcessAsync(tsxCommand, tsArgs, tsIndexerRoot);
                if (exitCode != 0 || _settings.Watch)
                    return exitCode;

                clearNextIndexer = false;
            }
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
        services.AddTransient<DocumentIngester>();
        services.AddTransient<IndexerPipeline>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IndexerPipeline>();

        await pipeline.RunAsync(_settings.RootPath, _settings.Project, clear, _settings.IncludeDocs, changedFiles, deletedFiles);

        if (!_settings.Watch)
            return 0;

        var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();
        logger.LogInformation(
            "Watch mode active - monitoring {Path} for .cs and documentation changes. Press Ctrl+C to exit.",
            _settings.RootPath.FullName);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        System.Timers.Timer? debounceTimer = null;
        var changedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ScheduleReindex(string fullPath, bool deleted)
        {
            var relativePath = Path.GetRelativePath(_settings.RootPath.FullName, fullPath).Replace('\\', '/');
            if (deleted)
                deletedDuringDebounce.Add(relativePath);
            else
                changedDuringDebounce.Add(relativePath);

            debounceTimer?.Stop();
            debounceTimer?.Dispose();
            debounceTimer = new System.Timers.Timer(2_000) { AutoReset = false };
            debounceTimer.Elapsed += async (_, _) =>
            {
                logger.LogInformation("[watch] Change detected - re-indexing...");
                var changedBatch = changedDuringDebounce.ToArray();
                var deletedBatch = deletedDuringDebounce.ToArray();
                changedDuringDebounce.Clear();
                deletedDuringDebounce.Clear();

                try
                {
                    await pipeline.RunAsync(
                        _settings.RootPath,
                        _settings.Project,
                        clear: false,
                        _settings.IncludeDocs,
                        changedFiles: changedBatch,
                        deletedFiles: deletedBatch,
                        cancellationToken: cts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[watch] Re-index failed.");
                }
            };
            debounceTimer.Start();
        }

        using var watcher = new FileSystemWatcher(_settings.RootPath.FullName)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        watcher.Filters.Add("*.cs");
        watcher.Filters.Add("*.md");
        watcher.Filters.Add("*.txt");
        watcher.Changed += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
        watcher.Created += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
        watcher.Deleted += (_, e) => ScheduleReindex(e.FullPath, deleted: true);
        watcher.Renamed += (_, e) =>
        {
            ScheduleReindex(e.OldFullPath, deleted: true);
            ScheduleReindex(e.FullPath, deleted: false);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user stops watch mode.
        }

        logger.LogInformation("Watch mode stopped.");
        return 0;
    }

    private static IEnumerable<FileInfo> EnumerateIndexableFiles(
        DirectoryInfo rootPath,
        bool includeCSharp,
        bool includeTypeScript,
        bool includeDocs)
    {
        return rootPath
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredPath(rootPath, file))
            .Where(file =>
                (includeCSharp && IsCSharpSourceFile(file)) ||
                (includeTypeScript && IsTypeScriptSourceFile(file)) ||
                (includeDocs && IsDocumentationFile(file)));
    }

    private static bool IsIgnoredPath(DirectoryInfo rootPath, FileInfo file)
    {
        var relPath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
        var segments = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals(".meridian", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("build", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("coverage", StringComparison.OrdinalIgnoreCase)) ||
               relPath.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
               relPath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               relPath.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCSharpSourceFile(FileInfo file) =>
        file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsTypeScriptSourceFile(FileInfo file) =>
        (file.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
         file.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) &&
        !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocumentationFile(FileInfo file) =>
        file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("ARCHITECTURE.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase);

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
            file => IsTypeScriptSourceFile(file) || (includeDocs && IsDocumentationFile(file))));

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
        DirectoryInfo languageRoot,
        IReadOnlyCollection<string> fullPaths)
    {
        var cacheDir = new DirectoryInfo(Path.Combine(_settings.RootPath.FullName, ".meridian", "cache"));
        cacheDir.Create();
        var file = new FileInfo(Path.Combine(
            cacheDir.FullName,
            $"ts-files-{Hash($"{_settings.Project}|{languageRoot.FullName}")}.txt"));

        File.WriteAllLines(file.FullName, fullPaths.Order(StringComparer.OrdinalIgnoreCase));
        return file;
    }

    private async Task DeleteProjectFilesAsync(IEnumerable<string> relativePaths)
    {
        var distinct = relativePaths
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetRelativePath(_settings.RootPath.FullName, path).Replace('\\', '/')
                : path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinct.Length == 0)
            return;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.CodeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var client = new CodeMeridianClient(httpClient);
        foreach (var relativePath in distinct)
            await client.DeleteProjectFileAsync(_settings.Project, relativePath);
    }

    private List<string> BuildTypeScriptIndexerArgs(DirectoryInfo tsIndexerRoot, DirectoryInfo rootPath)
    {
        return
        [
            Path.Combine(tsIndexerRoot.FullName, "src", "index.ts"),
            rootPath.FullName,
            "--project",
            _settings.Project,
        ];
    }

    private void AddTypeScriptIndexerOptions(
        List<string> arguments,
        bool clear,
        bool includeDocs,
        FileInfo? filesList)
    {
        arguments.AddRange(["--url", _settings.CodeMeridianUrl]);
        if (clear)
            arguments.Add("--clear");
        if (!includeDocs)
            arguments.Add("--no-docs");
        if (_settings.Watch)
            arguments.Add("--watch");
        if (filesList is not null)
            arguments.AddRange(["--files-list", filesList.FullName]);
    }

    private static async Task<int> EnsureTypeScriptIndexerDependenciesAsync(DirectoryInfo tsIndexerRoot)
    {
        var localTsx = Path.Combine(
            tsIndexerRoot.FullName,
            "node_modules",
            ".bin",
            OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

        if (File.Exists(localTsx))
            return 0;

        if (!File.Exists(Path.Combine(tsIndexerRoot.FullName, "package.json")))
            return 0;

        Console.WriteLine();
        Console.WriteLine("TypeScript indexer dependencies not found. Restoring npm packages...");

        var packageLock = Path.Combine(tsIndexerRoot.FullName, "package-lock.json");
        var arguments = File.Exists(packageLock)
            ? new[] { "ci", "--silent" }
            : ["install", "--silent"];

        return await RunProcessAsync(ExternalCommandResolver.NpmCommand(), arguments, tsIndexerRoot);
    }

    private static async Task<int> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        DirectoryInfo workingDirectory)
    {
        Console.WriteLine();
        Console.WriteLine($"> {fileName} {string.Join(' ', arguments.Select(QuoteIfNeeded))}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory.FullName,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
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

    private static string? ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
    {
        var localTsx = Path.Combine(
            tsIndexerRoot.FullName,
            "node_modules",
            ".bin",
            OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

        return File.Exists(localTsx) ? localTsx : null;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

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
}
