using System.Diagnostics;
using CodeMeridian.Indexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var rawArgs = args.ToList();
if (rawArgs.Count > 0 && rawArgs[0].Equals("index", StringComparison.OrdinalIgnoreCase))
    rawArgs.RemoveAt(0);

if (rawArgs.Count > 0 && rawArgs[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

LoadDotEnv(new DirectoryInfo(Directory.GetCurrentDirectory()));

var positional = new List<string>();
string? project = null;
var codeMeridianUrl = Environment.GetEnvironmentVariable("CodeMeridian_Url") ?? "http://localhost:5100";
var apiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");
var clear = false;
var includeDocs = true;
var watch = false;
var dryRun = false;
var listCapabilities = false;
var skipCSharp = false;
var skipTypeScript = false;
var skipDiagnostics = true;

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
        case "--clear":
            clear = true;
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

LoadDotEnv(rootPath);
codeMeridianUrl = codeMeridianUrlFromEnvironment(codeMeridianUrl);
apiKey ??= Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");

if (!rootPath.Exists)
{
    Console.Error.WriteLine($"error: directory not found: {rootPath.FullName}");
    return 1;
}

project ??= ResolveProjectName(rootPath);

var hasCSharp = !skipCSharp && ContainsFile(rootPath, ".cs");
var typeScriptRoots = skipTypeScript ? [] : FindTypeScriptRoots(rootPath);
var hasTypeScript = typeScriptRoots.Count > 0;

Console.WriteLine("CodeMeridian index");
Console.WriteLine($"  Root    : {rootPath.FullName}");
Console.WriteLine($"  Project : {project}");
Console.WriteLine($"  Server  : {codeMeridianUrl}");

if (!hasCSharp && !hasTypeScript)
{
    Console.WriteLine("No enabled indexers found matching this project.");
    Console.WriteLine("Use --list-capabilities to inspect available indexers.");
    return 0;
}

if (!skipDiagnostics)
{
    Console.WriteLine("warn: diagnostics indexing is not implemented yet; continuing with code/docs indexing.");
}

if (dryRun)
{
    PrintDryRun(rootPath, project, hasCSharp, typeScriptRoots, includeDocs, clear, watch, skipDiagnostics);
    return 0;
}

var exitCode = 0;
var clearNextIndexer = clear;

if (hasCSharp)
{
    exitCode = await RunCSharpIndexerAsync(
        rootPath,
        project,
        codeMeridianUrl,
        apiKey,
        clearNextIndexer,
        includeDocs,
        watch);

    if (exitCode != 0 || watch)
        return exitCode;

    clearNextIndexer = false;
}

if (hasTypeScript)
{
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
    foreach (var typeScriptRoot in typeScriptRoots)
    {
        var tsArgs = BuildTypeScriptIndexerArgs(tsIndexerRoot, typeScriptRoot, project);
        AddTypeScriptIndexerOptions(
            tsArgs,
            codeMeridianUrl,
            clearNextIndexer,
            includeDocs && !hasCSharp && typeScriptRoots.Count == 1,
            watch);

        if (tsxCommand.UseNpx)
            tsArgs.Insert(0, "tsx");

        exitCode = await RunAsync(tsxCommand.FileName, tsArgs, tsIndexerRoot);
        if (exitCode != 0 || watch)
            return exitCode;

        clearNextIndexer = false;
    }
}

return exitCode;

static async Task<int> RunCSharpIndexerAsync(
    DirectoryInfo rootPath,
    string project,
    string codeMeridianUrl,
    string? apiKey,
    bool clear,
    bool includeDocs,
    bool watch)
{
    var services = new ServiceCollection();

    services.AddLogging(builder => builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information));

    services.AddCodeMeridianClient(codeMeridianUrl, apiKey);
    services.AddTransient<CSharpIndexer>();
    services.AddTransient<DocumentIngester>();
    services.AddTransient<IndexerPipeline>();

    await using var provider = services.BuildServiceProvider();
    var pipeline = provider.GetRequiredService<IndexerPipeline>();

    await pipeline.RunAsync(rootPath, project, clear, includeDocs);

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

    void ScheduleReindex()
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();
        debounceTimer = new System.Timers.Timer(2_000) { AutoReset = false };
        debounceTimer.Elapsed += async (_, _) =>
        {
            logger.LogInformation("[watch] Change detected - re-indexing...");
            try
            {
                await pipeline.RunAsync(rootPath, project, clear: false, includeDocs, cts.Token);
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
    watcher.Changed += (_, _) => ScheduleReindex();
    watcher.Created += (_, _) => ScheduleReindex();
    watcher.Deleted += (_, _) => ScheduleReindex();
    watcher.Renamed += (_, _) => ScheduleReindex();

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
    bool watch)
{
    arguments.AddRange(["--url", codeMeridianUrl]);
    if (clear)
        arguments.Add("--clear");
    if (!includeDocs)
        arguments.Add("--no-docs");
    if (watch)
        arguments.Add("--watch");
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

static bool ContainsFile(DirectoryInfo root, params string[] extensions)
{
    var pending = new Stack<DirectoryInfo>();
    pending.Push(root);

    while (pending.Count > 0)
    {
        var current = pending.Pop();
        if (ShouldSkipDirectory(current))
            continue;

        foreach (var file in SafeEnumerateFiles(current))
        {
            if (extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
                && !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var directory in SafeEnumerateDirectories(current))
            pending.Push(directory);
    }

    return false;
}

static IReadOnlyList<DirectoryInfo> FindTypeScriptRoots(DirectoryInfo root)
{
    var roots = new List<DirectoryInfo>();

    foreach (var directory in EnumerateDirectoriesDepthFirst(root))
    {
        if (ShouldSkipDirectory(directory))
            continue;

        if (File.Exists(Path.Combine(directory.FullName, "tsconfig.json"))
            && ContainsFile(directory, ".ts", ".tsx"))
        {
            roots.Add(directory);
        }
    }

    if (roots.Count == 0 && ContainsFile(root, ".ts", ".tsx"))
        roots.Add(root);

    return roots
        .Where(candidate => !roots.Any(other =>
            !ReferenceEquals(candidate, other)
            && IsSubdirectoryOf(candidate, other)))
        .OrderBy(directory => directory.FullName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IEnumerable<DirectoryInfo> EnumerateDirectoriesDepthFirst(DirectoryInfo root)
{
    var pending = new Stack<DirectoryInfo>();
    pending.Push(root);

    while (pending.Count > 0)
    {
        var current = pending.Pop();
        yield return current;

        if (ShouldSkipDirectory(current))
            continue;

        foreach (var directory in SafeEnumerateDirectories(current))
            pending.Push(directory);
    }
}

static bool IsSubdirectoryOf(DirectoryInfo candidate, DirectoryInfo parent)
{
    var relative = Path.GetRelativePath(parent.FullName, candidate.FullName);
    return relative != "."
        && !relative.StartsWith("..", StringComparison.Ordinal)
        && !Path.IsPathRooted(relative);
}

static bool ShouldSkipDirectory(DirectoryInfo directory)
{
    var name = directory.Name;
    return name is ".git" or ".vs" or ".vscode" or "bin" or "obj" or "node_modules" or "dist" or "build" or "coverage";
}

static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory)
{
    try { return directory.EnumerateFiles(); }
    catch (UnauthorizedAccessException) { return []; }
    catch (DirectoryNotFoundException) { return []; }
}

static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
{
    try { return directory.EnumerateDirectories(); }
    catch (UnauthorizedAccessException) { return []; }
    catch (DirectoryNotFoundException) { return []; }
}

static DirectoryInfo? ResolveTypeScriptIndexerRoot()
{
    var packagedRoot = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "tools", "TsIndexer"));
    if (File.Exists(Path.Combine(packagedRoot.FullName, "src", "index.ts")))
        return packagedRoot;

    var repositoryRoot = FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
        ?? FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));

    if (repositoryRoot is null)
        return null;

    var sourceRoot = new DirectoryInfo(Path.Combine(repositoryRoot.FullName, "tools", "TsIndexer"));
    return File.Exists(Path.Combine(sourceRoot.FullName, "src", "index.ts")) ? sourceRoot : null;
}

static DirectoryInfo? FindRepositoryRoot(DirectoryInfo start)
{
    for (var current = start; current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "CodeMeridian.sln")))
            return current;
    }

    return null;
}

static string ResolveProjectName(DirectoryInfo root)
{
    var packageJson = new FileInfo(Path.Combine(root.FullName, "package.json"));
    if (packageJson.Exists)
    {
        var name = TryReadPackageName(packageJson);
        if (!string.IsNullOrWhiteSpace(name))
            return name;
    }

    var sln = root.GetFiles("*.sln").FirstOrDefault();
    if (sln is not null) return Path.GetFileNameWithoutExtension(sln.Name);

    var slnx = root.GetFiles("*.slnx").FirstOrDefault();
    if (slnx is not null) return Path.GetFileNameWithoutExtension(slnx.Name);

    var workspace = root.GetFiles("*.code-workspace").FirstOrDefault();
    if (workspace is not null) return Path.GetFileNameWithoutExtension(workspace.Name);

    return root.Name;
}

static string? TryReadPackageName(FileInfo packageJson)
{
    try
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJson.FullName));
        return document.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
    }
    catch
    {
        return null;
    }
}

static void LoadDotEnv(DirectoryInfo startDirectory)
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

        if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable(key) is not null)
            continue;

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"");
        else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1];

        Environment.SetEnvironmentVariable(key, value);
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
    bool skipDiagnostics)
{
    Console.WriteLine();
    Console.WriteLine("Dry run:");
    Console.WriteLine($"  Clear first       : {clear}");
    Console.WriteLine($"  Include docs      : {includeDocs}");
    Console.WriteLine($"  Watch mode        : {watch}");
    Console.WriteLine($"  Diagnostics       : {(skipDiagnostics ? "skipped" : "requested (not implemented)")}");
    Console.WriteLine($"  C# indexer        : {(hasCSharp ? "enabled" : "not applicable")}");
    Console.WriteLine($"  TypeScript roots  : {(typeScriptRoots.Count == 0 ? "none" : typeScriptRoots.Count)}");

    foreach (var typeScriptRoot in typeScriptRoots)
        Console.WriteLine($"    - {Path.GetRelativePath(rootPath.FullName, typeScriptRoot.FullName)}");

    Console.WriteLine($"  Project context   : {project}");
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
    Console.WriteLine("  Diagnostics      planned - not implemented yet");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  codemeridian index .");
    Console.WriteLine("  codemeridian index . --project MyProject --watch");
    Console.WriteLine("  codemeridian index . --dry-run");
}

static string QuoteIfNeeded(string value)
{
    return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

static string NpxCommand() => OperatingSystem.IsWindows() ? "npx.cmd" : "npx";

static string NpmCommand() => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

static (string FileName, bool UseNpx) ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
{
    var localTsx = Path.Combine(
        tsIndexerRoot.FullName,
        "node_modules",
        ".bin",
        OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

    return File.Exists(localTsx) ? (localTsx, false) : (NpxCommand(), true);
}

static string codeMeridianUrlFromEnvironment(string fallback) =>
    Environment.GetEnvironmentVariable("CodeMeridian_Url") ?? fallback;

static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian Indexer - unified CLI for indexing codebases into CodeMeridian.

        USAGE:
          codemeridian index [path] [--project <name>] [options]

        BACKWARD-COMPATIBLE SOURCE USAGE:
          dotnet run --project tools/IndexerAll -- [path] [--project <name>] [options]

        ARGUMENTS:
          [path]                 Root directory to scan. Defaults to the shell's current directory.

        OPTIONS:
          --project <name>       Project context name. If omitted, auto-detected from the target root.
          --url <url>            CodeMeridian server URL. Alias for --CodeMeridian.
          --CodeMeridian <url>   CodeMeridian server URL.
          --clear                Remove existing knowledge before indexing. Applied only once.
          --skip-csharp          Skip C# indexing.
          --skip-typescript      Skip TypeScript/TSX indexing.
          --no-docs              Skip documentation ingestion. Alias: --skip-docs.
          --include-diagnostics  Reserved for diagnostics indexing; currently prints a warning.
          --skip-diagnostics     Skip diagnostics indexing. This is the current default.
          --dry-run              Show what would be indexed without ingesting anything.
          --list-capabilities    Show available indexers on this machine.
          --watch                Watch mode. If both languages are present, C# watch runs first.
          -h, --help             Show this help.

        EXAMPLES:
          codemeridian index .
          codemeridian index C:\Projects\MyApi --project MyApi
          codemeridian index . --clear --watch
          codemeridian index . --dry-run
        """);
}
