using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var rawArgs = args.ToList();
var command = "index";
if (rawArgs.Count > 0
    && (rawArgs[0].Equals("index", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("clear", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("init", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("serve", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("doctor", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("check-drift", StringComparison.OrdinalIgnoreCase)))
{
    command = rawArgs[0].ToLowerInvariant();
    rawArgs.RemoveAt(0);
}

if (rawArgs.Count > 0 && rawArgs[0] is "-h" or "--help" or "help")
{
    if (command == "clear")
        ClearCommand.PrintUsage();
    else if (command == "serve")
        ServeCommand.PrintUsage();
    else
        PrintUsage();
    return 0;
}

var initialEnvironmentKeys = GetEnvironmentKeys();
var loadedDotEnvKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
LoadDotEnv(new DirectoryInfo(Directory.GetCurrentDirectory()), initialEnvironmentKeys, loadedDotEnvKeys);
var startupConfig = IndexerConfig.Load(new DirectoryInfo(Directory.GetCurrentDirectory()));

if (command == "clear")
    return await ClearCommand.RunAsync(rawArgs, startupConfig);

if (command == "serve")
    return await ServeCommand.RunAsync(rawArgs);

var positional = new List<string>();
string? project = null;
string? codeMeridianUrlOverride = null;
var clear = false;
var initForce = false;
var includeDocs = true;
var watch = false;
var dryRun = false;
var listCapabilities = false;
var skipCSharp = false;
var skipTypeScript = false;
var skipDiagnostics = false;
var allowRepoScripts = false;
var incremental = true;
var verify = false;
var failOn = "high";

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
            codeMeridianUrlOverride = rawArgs[++i];
            break;
        case "--clear":
            clear = true;
            break;
        case "--force":
            initForce = true;
            break;
        case "--no-docs":
        case "--skip-docs":
            includeDocs = false;
            break;
        case "--watch":
            watch = true;
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "--list-capabilities":
            listCapabilities = true;
            break;
        case "--skip-csharp":
            skipCSharp = true;
            break;
        case "--skip-typescript":
            skipTypeScript = true;
            break;
        case "--include-diagnostics":
            skipDiagnostics = false;
            break;
        case "--skip-diagnostics":
            skipDiagnostics = true;
            break;
        case "--allow-repo-scripts":
            allowRepoScripts = true;
            break;
        case "--verify":
            verify = true;
            break;
        case "--fail-on" when i + 1 < rawArgs.Count:
            failOn = rawArgs[++i];
            break;
        case "--no-incremental":
        case "--force-full":
            incremental = false;
            break;
        default:
            if (!rawArgs[i].StartsWith("-", StringComparison.Ordinal))
                positional.Add(rawArgs[i]);
            else
                Console.Error.WriteLine($"warn: unknown option ignored: {rawArgs[i]}");
            break;
    }
}

if (listCapabilities)
{
    PrintCapabilities();
    return 0;
}

var rootPath = new DirectoryInfo(positional.Count > 0
    ? Path.GetFullPath(positional[0], Directory.GetCurrentDirectory())
    : Directory.GetCurrentDirectory());

LoadDotEnv(rootPath, initialEnvironmentKeys, loadedDotEnvKeys, overwriteLoadedValues: true);

var meridianConfig = IndexerConfig.Load(rootPath);
var codeMeridianUrl = ResolveCodeMeridianUrl(codeMeridianUrlOverride, meridianConfig);
var apiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");
var environmentProject = Environment.GetEnvironmentVariable("CodeMeridian_Project");
allowRepoScripts |= meridianConfig?.AllowRepoScripts ?? false;

if (command == "init")
    return InitCommand.Run(rootPath, project ?? environmentProject, codeMeridianUrl, initForce);

if (command == "doctor")
    return await StatusCommand.RunDoctorAsync(project ?? environmentProject ?? meridianConfig?.Project ?? IndexerDiscovery.ResolveProjectName(rootPath), codeMeridianUrl, apiKey);

if (command == "check-drift" || verify)
    return await StatusCommand.RunDriftVerificationAsync(
        project ?? environmentProject ?? meridianConfig?.Project ?? IndexerDiscovery.ResolveProjectName(rootPath),
        codeMeridianUrl,
        apiKey,
        failOn);

if (!rootPath.Exists)
{
    Console.Error.WriteLine($"error: directory not found: {rootPath.FullName}");
    return 1;
}

project ??= environmentProject ?? meridianConfig?.Project ?? IndexerDiscovery.ResolveProjectName(rootPath);

var hasCSharp = !skipCSharp && IndexerDiscovery.ContainsFile(rootPath, ".cs");
var typeScriptRoots = skipTypeScript ? [] : IndexerDiscovery.FindTypeScriptRoots(rootPath);
var hasTypeScript = typeScriptRoots.Count > 0;
var cache = IncrementalIndexCache.Load(rootPath, project);
var indexableFiles = EnumerateIndexableFiles(rootPath, hasCSharp, hasTypeScript, includeDocs).ToArray();
var incrementalPlan = cache.BuildPlan(rootPath, indexableFiles, forceFull: clear || !incremental);
var changedFiles = incremental && !clear ? incrementalPlan.ChangedFiles : null;
var deletedFiles = incremental && !clear ? incrementalPlan.DeletedFiles : [];

Console.WriteLine("CodeMeridian index");
Console.WriteLine($"  Root    : {rootPath.FullName}");
Console.WriteLine($"  Project : {project}");
Console.WriteLine($"  Server  : {codeMeridianUrl}");
Console.WriteLine($"  Mode    : {(incremental && !clear ? "incremental" : "full")}");

if (!hasCSharp && !hasTypeScript)
{
    Console.WriteLine("No enabled indexers found matching this project.");
    Console.WriteLine("Use --list-capabilities to inspect available indexers.");
    return 0;
}

if (dryRun)
{
    PrintDryRun(rootPath, project, hasCSharp, typeScriptRoots, includeDocs, clear, watch, skipDiagnostics, incrementalPlan, incremental && !clear);
    return 0;
}

var exitCode = 0;
var clearNextIndexer = clear;

if (incremental && !clear && !incrementalPlan.HasChanges)
{
    Console.WriteLine("No file changes detected since the last successful index run.");
    return 0;
}

if (hasCSharp)
{
    exitCode = await RunCSharpIndexerAsync(
        rootPath,
        project,
        codeMeridianUrl,
        apiKey,
        clearNextIndexer,
        includeDocs,
        watch,
        changedFiles,
        deletedFiles);

    if (exitCode != 0 || watch)
        return exitCode;

    clearNextIndexer = false;
}

if (hasTypeScript)
{
    if (changedFiles is not null || deletedFiles.Count > 0)
        await DeleteProjectFilesAsync(
            rootPath,
            project,
            codeMeridianUrl,
            apiKey,
            FilterTypeScriptIndexerFiles(changedFiles ?? [], rootPath, typeScriptRoots, includeDocs && !hasCSharp)
                .Concat(FilterTypeScriptIndexerFiles(deletedFiles, rootPath, typeScriptRoots, includeDocs && !hasCSharp)));

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
                rootPath,
                typeScriptRoot,
                file => IsTypeScriptSourceFile(file) || (includeDocs && !hasCSharp && IsDocumentationFile(file)))
                .ToArray();

        if (changedTypeScriptFiles is { Length: 0 })
            continue;

        var filesList = changedTypeScriptFiles is null
            ? null
            : WriteFilesList(rootPath, project, typeScriptRoot, changedTypeScriptFiles);

        var tsArgs = BuildTypeScriptIndexerArgs(tsIndexerRoot, typeScriptRoot, project);
        AddTypeScriptIndexerOptions(
            tsArgs,
            codeMeridianUrl,
            clearNextIndexer,
            includeDocs && !hasCSharp && typeScriptRoots.Count == 1,
            watch,
            filesList);

        exitCode = await RunAsync(tsxCommand, tsArgs, tsIndexerRoot);
        if (exitCode != 0 || watch)
            return exitCode;

        clearNextIndexer = false;
    }
}

if (!skipDiagnostics)
{
    exitCode = await DiagnosticsCommand.RunAsync(rootPath, typeScriptRoots, project, codeMeridianUrl, apiKey, allowRepoScripts);
    if (exitCode != 0)
        return exitCode;
}

if (exitCode == 0)
    cache.Save(incrementalPlan);

return exitCode;

static async Task<int> RunCSharpIndexerAsync(
    DirectoryInfo rootPath,
    string project,
    string codeMeridianUrl,
    string? apiKey,
    bool clear,
    bool includeDocs,
    bool watch,
    IReadOnlyCollection<string>? changedFiles,
    IReadOnlyCollection<string> deletedFiles)
{
    var services = new ServiceCollection();

    services.AddLogging(builder => builder
        .AddConsole()
        .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
        .AddFilter("Microsoft", LogLevel.Warning)
        .SetMinimumLevel(LogLevel.Information));

    services.AddCodeMeridianClient(codeMeridianUrl, apiKey);
    services.AddTransient<CSharpIndexer>();
    services.AddTransient<DocumentIngester>();
    services.AddTransient<IndexerPipeline>();

    await using var provider = services.BuildServiceProvider();
    var pipeline = provider.GetRequiredService<IndexerPipeline>();

    await pipeline.RunAsync(rootPath, project, clear, includeDocs, changedFiles, deletedFiles);

    if (!watch)
        return 0;

    var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();
    logger.LogInformation(
        "Watch mode active - monitoring {Path} for .cs and documentation changes. Press Ctrl+C to exit.",
        rootPath.FullName);

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
        var relativePath = Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
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
                    rootPath,
                    project,
                    clear: false,
                    includeDocs,
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

    using var watcher = new FileSystemWatcher(rootPath.FullName)
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

static List<string> BuildTypeScriptIndexerArgs(DirectoryInfo tsIndexerRoot, DirectoryInfo rootPath, string project)
{
    return
    [
        Path.Combine(tsIndexerRoot.FullName, "src", "index.ts"),
        rootPath.FullName,
        "--project",
        project,
    ];
}

static void AddTypeScriptIndexerOptions(
    List<string> arguments,
    string codeMeridianUrl,
    bool clear,
    bool includeDocs,
    bool watch,
    FileInfo? filesList)
{
    arguments.AddRange(["--url", codeMeridianUrl]);
    if (clear)
        arguments.Add("--clear");
    if (!includeDocs)
        arguments.Add("--no-docs");
    if (watch)
        arguments.Add("--watch");
    if (filesList is not null)
        arguments.AddRange(["--files-list", filesList.FullName]);
}

static async Task<int> EnsureTypeScriptIndexerDependenciesAsync(DirectoryInfo tsIndexerRoot)
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

    return await RunAsync(NpmCommand(), arguments, tsIndexerRoot);
}

static async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, DirectoryInfo workingDirectory)
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

static DirectoryInfo? ResolveTypeScriptIndexerRoot()
{
    var repositoryRoot = IndexerDiscovery.FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
        ?? IndexerDiscovery.FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));

    if (repositoryRoot is not null)
    {
        var sourceRoot = new DirectoryInfo(Path.Combine(repositoryRoot.FullName, "tools", "TsIndexer"));
        if (File.Exists(Path.Combine(sourceRoot.FullName, "src", "index.ts")))
            return sourceRoot;
    }

    var packagedRoot = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "tools", "TsIndexer"));
    return File.Exists(Path.Combine(packagedRoot.FullName, "src", "index.ts")) ? packagedRoot : null;
}

static void LoadDotEnv(
    DirectoryInfo startDirectory,
    IReadOnlySet<string> initiallyPresentKeys,
    ISet<string> loadedDotEnvKeys,
    bool overwriteLoadedValues = false)
{
    var envFile = FindDotEnv(startDirectory);
    if (envFile is null)
        return;

    foreach (var rawLine in File.ReadLines(envFile.FullName))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            line = line["export ".Length..].TrimStart();

        var separator = line.IndexOf('=');
        if (separator <= 0)
            continue;

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(key))
            continue;

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"");
        else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1];

        var currentValue = Environment.GetEnvironmentVariable(key);
        if (currentValue is not null)
        {
            if (initiallyPresentKeys.Contains(key))
                continue;

            if (!overwriteLoadedValues)
                continue;
        }

        Environment.SetEnvironmentVariable(key, value);
        loadedDotEnvKeys.Add(key);
    }
}

static FileInfo? FindDotEnv(DirectoryInfo directory)
{
    for (var current = directory; current is not null; current = current.Parent)
    {
        var envFile = new FileInfo(Path.Combine(current.FullName, ".env"));
        if (envFile.Exists)
            return envFile;
    }

    return null;
}

static void PrintDryRun(
    DirectoryInfo rootPath,
    string project,
    bool hasCSharp,
    IReadOnlyList<DirectoryInfo> typeScriptRoots,
    bool includeDocs,
    bool clear,
    bool watch,
    bool skipDiagnostics,
    IncrementalIndexPlan incrementalPlan,
    bool incremental)
{
    Console.WriteLine();
    Console.WriteLine("Dry run:");
    Console.WriteLine($"  Clear first       : {clear}");
    Console.WriteLine($"  Incremental       : {(incremental ? "enabled" : "disabled")}");
    Console.WriteLine($"  Changed files     : {incrementalPlan.ChangedFiles.Count}");
    Console.WriteLine($"  Deleted files     : {incrementalPlan.DeletedFiles.Count}");
    Console.WriteLine($"  Include docs      : {includeDocs}");
    Console.WriteLine($"  Watch mode        : {watch}");
    Console.WriteLine($"  Diagnostics       : {(skipDiagnostics ? "skipped" : "enabled")}");
    Console.WriteLine($"  C# indexer        : {(hasCSharp ? "enabled" : "not applicable")}");
    Console.WriteLine($"  TypeScript roots  : {(typeScriptRoots.Count == 0 ? "none" : typeScriptRoots.Count)}");

    foreach (var typeScriptRoot in typeScriptRoots)
        Console.WriteLine($"    - {Path.GetRelativePath(rootPath.FullName, typeScriptRoot.FullName)}");

    Console.WriteLine($"  Project context   : {project}");
}

static IEnumerable<FileInfo> EnumerateIndexableFiles(
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

static bool IsIgnoredPath(DirectoryInfo rootPath, FileInfo file)
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

static bool IsCSharpSourceFile(FileInfo file) =>
    file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

static bool IsTypeScriptSourceFile(FileInfo file) =>
    (file.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
     file.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) &&
    !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

static bool IsDocumentationFile(FileInfo file) =>
    file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
    file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("ARCHITECTURE.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase);

static IEnumerable<string> FilterTypeScriptIndexerFiles(
    IEnumerable<string> relativePaths,
    DirectoryInfo rootPath,
    IReadOnlyCollection<DirectoryInfo> typeScriptRoots,
    bool includeDocs) =>
    FilterFilesForRoots(
        relativePaths,
        rootPath,
        typeScriptRoots,
        file => IsTypeScriptSourceFile(file) || (includeDocs && IsDocumentationFile(file)));

static IEnumerable<string> FilterFilesForRoots(
    IEnumerable<string> relativePaths,
    DirectoryInfo rootPath,
    IReadOnlyCollection<DirectoryInfo> roots,
    Func<FileInfo, bool> predicate) =>
    roots.SelectMany(root => FilterFilesForRoot(relativePaths, rootPath, root, predicate));

static IEnumerable<string> FilterFilesForRoot(
    IEnumerable<string> relativePaths,
    DirectoryInfo rootPath,
    DirectoryInfo targetRoot,
    Func<FileInfo, bool> predicate)
{
    foreach (var relativePath in relativePaths)
    {
        var fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), rootPath.FullName);
        if (!fullPath.StartsWith(targetRoot.FullName, StringComparison.OrdinalIgnoreCase))
            continue;

        var file = new FileInfo(fullPath);
        if (predicate(file))
            yield return fullPath;
    }
}

static FileInfo WriteFilesList(
    DirectoryInfo rootPath,
    string project,
    DirectoryInfo languageRoot,
    IReadOnlyCollection<string> fullPaths)
{
    var cacheDir = new DirectoryInfo(Path.Combine(rootPath.FullName, ".meridian", "cache"));
    cacheDir.Create();
    var file = new FileInfo(Path.Combine(
        cacheDir.FullName,
        $"ts-files-{Hash($"{project}|{languageRoot.FullName}")}.txt"));

    File.WriteAllLines(file.FullName, fullPaths.Order(StringComparer.OrdinalIgnoreCase));
    return file;
}

static async Task DeleteProjectFilesAsync(
    DirectoryInfo rootPath,
    string project,
    string codeMeridianUrl,
    string? apiKey,
    IEnumerable<string> relativePaths)
{
    var distinct = relativePaths
        .Select(path => Path.IsPathRooted(path)
            ? Path.GetRelativePath(rootPath.FullName, path).Replace('\\', '/')
            : path.Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (distinct.Length == 0)
        return;

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(codeMeridianUrl),
        Timeout = TimeSpan.FromMinutes(10)
    };
    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    if (!string.IsNullOrWhiteSpace(apiKey))
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var client = new CodeMeridianClient(httpClient);
    foreach (var relativePath in distinct)
        await client.DeleteProjectFileAsync(project, relativePath);
}

static void PrintCapabilities()
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
    Console.WriteLine("  codemeridian serve");
    Console.WriteLine("  codemeridian doctor --project MyProject");
    Console.WriteLine("  codemeridian check-drift --project MyProject --fail-on high");
    Console.WriteLine("  codemeridian clear --project MyProject");
    Console.WriteLine("  codemeridian clear --all-code-graph");
}

static string ResolveCodeMeridianUrl(string? overrideUrl, IndexerConfig? config) =>
    !string.IsNullOrWhiteSpace(overrideUrl)
        ? overrideUrl
        : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CodeMeridian_Url"))
            ? Environment.GetEnvironmentVariable("CodeMeridian_Url")!
            : !string.IsNullOrWhiteSpace(config?.CodeMeridianUrl)
                ? config!.CodeMeridianUrl!
                : "http://localhost:5100";

static HashSet<string> GetEnvironmentKeys() =>
    Environment.GetEnvironmentVariables()
        .Keys
        .Cast<object>()
        .Select(key => key.ToString() ?? string.Empty)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

static string QuoteIfNeeded(string value)
{
    return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

static string NpmCommand() => ResolveCommandFromPath(OperatingSystem.IsWindows() ? "npm.cmd" : "npm");

static string ResolveCommandFromPath(string command)
{
    if (Path.IsPathRooted(command))
        return command;

    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
        return command;

    foreach (var directory in path.Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(directory))
            continue;

        try
        {
            var candidate = Path.Combine(directory.Trim('"'), command);
            if (File.Exists(candidate))
                return candidate;
        }
        catch
        {
            // Ignore malformed PATH entries.
        }
    }

    return command;
}

static string? ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
{
    var localTsx = Path.Combine(
        tsIndexerRoot.FullName,
        "node_modules",
        ".bin",
        OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

    return File.Exists(localTsx) ? localTsx : null;
}

static string Hash(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
}

static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian Indexer - unified CLI for indexing codebases into CodeMeridian.

        USAGE:
          codemeridian index [path] [--project <name>] [options]
          codemeridian init [path] [--project <name>] [--url <url>] [--force]
          codemeridian serve [path] [--host <host>] [--port <port>] [--no-start]
          codemeridian doctor [--project <name>] [--url <url>]
          codemeridian check-drift [path] [--project <name>] [--url <url>] [--fail-on <severity>]
          codemeridian clear --project <name>
          codemeridian clear --all-code-graph

        BACKWARD-COMPATIBLE SOURCE USAGE:
          dotnet run --project tools/Indexer -- [path] [--project <name>] [options]
          dotnet run --project tools/Indexer -- init [path] [--project <name>] [--url <url>] [--force]
          dotnet run --project tools/Indexer -- serve [path] [--no-start]
          dotnet run --project tools/Indexer -- doctor [--project <name>] [--url <url>]
          dotnet run --project tools/Indexer -- check-drift [path] [--project <name>] [--url <url>] [--fail-on <severity>]
          dotnet run --project tools/Indexer -- clear --project <name>

        ARGUMENTS:
          [path]                 Root directory to scan. Defaults to the shell's current directory.

        OPTIONS:
          --project <name>       Project context name. If omitted, auto-detected from the target root.
          --url <url>            CodeMeridian server URL. Alias for --CodeMeridian.
          --CodeMeridian <url>   CodeMeridian server URL.
          --clear                Remove existing knowledge before indexing. Applied only once.
          --force                Overwrite generated config when running init or serve.
          --skip-csharp          Skip C# indexing.
          --skip-typescript      Skip TypeScript/TSX indexing.
          --no-docs              Skip documentation ingestion. Alias: --skip-docs.
          --include-diagnostics  Run diagnostics indexing. This is the default; kept for compatibility.
          --skip-diagnostics     Skip project-native compiler, TypeScript, and lint diagnostics indexing.
          --allow-repo-scripts   Allow repo-controlled build and lint commands during diagnostics.
          --verify               Skip indexing and only verify graph drift/freshness. Alias for check-drift mode.
          --fail-on <severity>   Drift threshold for verification mode: low, moderate, or high. Default: high.
          --no-incremental       Ignore .meridian/cache and scan all enabled files. Alias: --force-full.
          --dry-run              Show what would be indexed without ingesting anything.
          --list-capabilities    Show available indexers on this machine.
          --watch                Watch mode. If both languages are present, C# watch runs first.
          -h, --help             Show this help.

        EXAMPLES:
          codemeridian index . --clear
          codemeridian index C:\Projects\MyApi --project MyApi --clear
          codemeridian init C:\Projects\MyApi
          codemeridian serve C:\Projects\MyApi --no-start
          codemeridian doctor --project CodeMeridian
          codemeridian check-drift --project CodeMeridian --fail-on high
          codemeridian clear --project MyApi
          codemeridian clear --all-code-graph
          codemeridian index . --clear --watch
          codemeridian index . --dry-run
        """);
}
