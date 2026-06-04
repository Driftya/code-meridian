using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var rawArgs = args.ToList();
var command = "index";
if (rawArgs.Count > 0
    && (rawArgs[0].Equals("index", StringComparison.OrdinalIgnoreCase)
        || rawArgs[0].Equals("clear", StringComparison.OrdinalIgnoreCase)))
{
    command = rawArgs[0].ToLowerInvariant();
    rawArgs.RemoveAt(0);
}

if (rawArgs.Count > 0 && rawArgs[0] is "-h" or "--help" or "help")
{
    if (command == "clear")
        PrintClearUsage();
    else
        PrintUsage();
    return 0;
}

LoadDotEnv(new DirectoryInfo(Directory.GetCurrentDirectory()));

if (command == "clear")
    return await RunClearCommandAsync(rawArgs);

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

project ??= IndexerDiscovery.ResolveProjectName(rootPath);

var hasCSharp = !skipCSharp && IndexerDiscovery.ContainsFile(rootPath, ".cs");
var typeScriptRoots = skipTypeScript ? [] : IndexerDiscovery.FindTypeScriptRoots(rootPath);
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

if (!skipDiagnostics)
{
    exitCode = await RunDiagnosticsAsync(rootPath, project, codeMeridianUrl, apiKey);
    if (exitCode != 0)
        return exitCode;
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

static async Task<int> RunDiagnosticsAsync(
    DirectoryInfo rootPath,
    string project,
    string codeMeridianUrl,
    string? apiKey)
{
    Console.WriteLine();
    Console.WriteLine("Indexing diagnostics...");

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(codeMeridianUrl),
        Timeout = TimeSpan.FromMinutes(10)
    };
    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    if (!string.IsNullOrWhiteSpace(apiKey))
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var client = new CodeMeridianClient(httpClient);
    await client.ClearProjectDiagnosticsAsync(project);

    var findings = new List<DiagnosticFinding>();

    if (IndexerDiscovery.ContainsFile(rootPath, ".cs"))
    {
        var build = await RunCaptureAsync("dotnet", ["build", "--no-restore", "--nologo"], rootPath);
        findings.AddRange(ParseDotnetDiagnostics(build.Output, rootPath, project));
    }

    if (File.Exists(Path.Combine(rootPath.FullName, "tsconfig.json")))
    {
        var tsc = ResolveLocalNodeBinary(rootPath, "tsc");
        if (tsc is not null)
        {
            var result = await RunCaptureAsync(tsc, ["--noEmit", "--pretty", "false"], rootPath);
            findings.AddRange(ParseTypeScriptDiagnostics(result.Output, rootPath, project));
        }
        else
        {
            Console.WriteLine("  TypeScript diagnostics unavailable: local tsc not found.");
        }
    }

    var lintCommand = ResolveLintCommand(rootPath);
    if (lintCommand is not null)
    {
        var result = await RunCaptureAsync(lintCommand.Value.FileName, lintCommand.Value.Arguments, rootPath);
        findings.AddRange(ParseLintDiagnostics(result.Output, rootPath, project));
    }

    var distinct = findings
        .GroupBy(f => f.Id, StringComparer.Ordinal)
        .Select(g => g.First())
        .ToArray();

    foreach (var finding in distinct)
    {
        await client.IngestCodeNodeAsync(
            finding.Id,
            $"{finding.Severity} {finding.Code}",
            "Diagnostic",
            namespacePath: finding.Source,
            filePath: finding.FilePath,
            lineNumber: finding.Line,
            summary: finding.Message,
            projectContext: project);

        if (!string.IsNullOrWhiteSpace(finding.FilePath))
        {
            await client.IngestRelationshipAsync(
                $"{project}::File::{finding.FilePath}",
                finding.Id,
                "Contains");
        }
    }

    Console.WriteLine($"  Indexed {distinct.Length} diagnostics.");
    return 0;
}

static async Task<(int ExitCode, string Output)> RunCaptureAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    DirectoryInfo workingDirectory)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory.FullName,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    foreach (var argument in arguments)
        process.StartInfo.ArgumentList.Add(argument);

    process.Start();
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, stdout + Environment.NewLine + stderr);
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
    Console.WriteLine("  codemeridian index . --clear");
    Console.WriteLine("  codemeridian index . --project MyProject --watch");
    Console.WriteLine("  codemeridian index . --dry-run");
    Console.WriteLine("  codemeridian clear --project MyProject");
    Console.WriteLine("  codemeridian clear --all-code-graph");
}

static string QuoteIfNeeded(string value)
{
    return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

static string NpxCommand() => ResolveCommandFromPath(OperatingSystem.IsWindows() ? "npx.cmd" : "npx");

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

static (string FileName, bool UseNpx) ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
{
    var localTsx = Path.Combine(
        tsIndexerRoot.FullName,
        "node_modules",
        ".bin",
        OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

    return File.Exists(localTsx) ? (localTsx, false) : (NpxCommand(), true);
}

static IReadOnlyList<DiagnosticFinding> ParseDotnetDiagnostics(
    string output,
    DirectoryInfo rootPath,
    string project)
{
    const string pattern = @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>warning|error)\s(?<code>[A-Z]+\d+):\s(?<message>.+?)(?:\s\[(?<project>.+?)\])?$";
    return ParseDiagnostics(output, rootPath, project, "dotnet", pattern);
}

static IReadOnlyList<DiagnosticFinding> ParseTypeScriptDiagnostics(
    string output,
    DirectoryInfo rootPath,
    string project)
{
    const string pattern = @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error)\s(?<code>TS\d+):\s(?<message>.+)$";
    return ParseDiagnostics(output, rootPath, project, "tsc", pattern);
}

static IReadOnlyList<DiagnosticFinding> ParseLintDiagnostics(
    string output,
    DirectoryInfo rootPath,
    string project)
{
    const string pattern = @"^\s*(?<line>\d+):(?<column>\d+)\s+(?<severity>error|warning|warn)\s+(?<message>.+?)\s+(?<code>[@\w/-]+)$";
    var findings = new List<DiagnosticFinding>();
    string? currentFile = null;

    foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
    {
        var line = rawLine.TrimEnd();
        if (!line.StartsWith(' ') && LooksLikePath(line))
        {
            currentFile = NormalizePath(line, rootPath);
            continue;
        }

        if (currentFile is null)
            continue;

        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            continue;

        findings.Add(CreateDiagnostic(
            project,
            "eslint",
            match.Groups["severity"].Value is "warn" ? "warning" : match.Groups["severity"].Value,
            match.Groups["code"].Value,
            match.Groups["message"].Value.Trim(),
            currentFile,
            ParseInt(match.Groups["line"].Value),
            ParseInt(match.Groups["column"].Value)));
    }

    return findings;
}

static IReadOnlyList<DiagnosticFinding> ParseDiagnostics(
    string output,
    DirectoryInfo rootPath,
    string project,
    string source,
    string pattern)
{
    var findings = new List<DiagnosticFinding>();

    foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
    {
        var match = Regex.Match(rawLine.TrimEnd(), pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            continue;

        findings.Add(CreateDiagnostic(
            project,
            source,
            match.Groups["severity"].Value,
            match.Groups["code"].Value,
            match.Groups["message"].Value.Trim(),
            NormalizePath(match.Groups["file"].Value, rootPath),
            ParseInt(match.Groups["line"].Value),
            ParseInt(match.Groups["column"].Value)));
    }

    return findings;
}

static DiagnosticFinding CreateDiagnostic(
    string project,
    string source,
    string severity,
    string code,
    string message,
    string filePath,
    int? line,
    int? column)
{
    var normalizedSeverity = severity.Equals("warn", StringComparison.OrdinalIgnoreCase)
        ? "warning"
        : severity.ToLowerInvariant();
    var hashInput = $"{project}|{source}|{normalizedSeverity}|{code}|{filePath}|{line}|{column}|{message}";
    var id = $"{project}::Diagnostic::{Hash(hashInput)}";
    return new DiagnosticFinding(id, normalizedSeverity, code, message, filePath, line, column, source);
}

static string? ResolveLocalNodeBinary(DirectoryInfo rootPath, string name)
{
    var executable = OperatingSystem.IsWindows() ? $"{name}.cmd" : name;
    for (var current = rootPath; current is not null; current = current.Parent)
    {
        var candidate = Path.Combine(current.FullName, "node_modules", ".bin", executable);
        if (File.Exists(candidate))
            return candidate;
    }

    return null;
}

static (string FileName, string[] Arguments)? ResolveLintCommand(DirectoryInfo rootPath)
{
    var packageJson = new FileInfo(Path.Combine(rootPath.FullName, "package.json"));
    if (packageJson.Exists)
    {
        var content = File.ReadAllText(packageJson.FullName);
        if (content.Contains("\"lint\"", StringComparison.OrdinalIgnoreCase))
            return (NpmCommand(), ["run", "lint"]);
    }

    var eslint = ResolveLocalNodeBinary(rootPath, "eslint");
    return eslint is null ? null : (eslint, ["."]);
}

static string NormalizePath(string path, DirectoryInfo rootPath)
{
    var trimmed = path.Trim().Trim('"');
    var fullPath = Path.IsPathRooted(trimmed)
        ? trimmed
        : Path.GetFullPath(trimmed, rootPath.FullName);

    return Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
}

static bool LooksLikePath(string value) =>
    value.Contains('/') || value.Contains('\\') || value.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
    value.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
    value.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

static int? ParseInt(string value) =>
    int.TryParse(value, out var parsed) ? parsed : null;

static string Hash(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
}

static string codeMeridianUrlFromEnvironment(string fallback) =>
    Environment.GetEnvironmentVariable("CodeMeridian_Url") ?? fallback;

static async Task<int> RunClearCommandAsync(IReadOnlyList<string> rawArgs)
{
    string? project = null;
    var clearAllCodeGraph = false;
    var codeMeridianUrl = Environment.GetEnvironmentVariable("CodeMeridian_Url") ?? "http://localhost:5100";
    var apiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");

    for (var i = 0; i < rawArgs.Count; i++)
    {
        switch (rawArgs[i])
        {
            case "-h":
            case "--help":
                PrintClearUsage();
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
        PrintClearUsage();
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

static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian Indexer - unified CLI for indexing codebases into CodeMeridian.

        USAGE:
          codemeridian index [path] [--project <name>] [options]
          codemeridian clear --project <name>
          codemeridian clear --all-code-graph

        BACKWARD-COMPATIBLE SOURCE USAGE:
          dotnet run --project tools/Indexer -- [path] [--project <name>] [options]
          dotnet run --project tools/Indexer -- clear --project <name>

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
          --include-diagnostics  Run project-native compiler, TypeScript, and lint diagnostics indexing.
          --skip-diagnostics     Skip diagnostics indexing. This is the current default.
          --dry-run              Show what would be indexed without ingesting anything.
          --list-capabilities    Show available indexers on this machine.
          --watch                Watch mode. If both languages are present, C# watch runs first.
          -h, --help             Show this help.

        EXAMPLES:
          codemeridian index . --clear
          codemeridian index C:\Projects\MyApi --project MyApi --clear
          codemeridian clear --project MyApi
          codemeridian clear --all-code-graph
          codemeridian index . --clear --watch
          codemeridian index . --dry-run
        """);
}

static void PrintClearUsage()
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

internal sealed record DiagnosticFinding(
    string Id,
    string Severity,
    string Code,
    string Message,
    string FilePath,
    int? Line,
    int? Column,
    string Source);
