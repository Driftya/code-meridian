using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// -- Parse args ----------------------------------------------------------------
if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var positional = new List<string>();
string? project = null;
LoadDotEnv();
var CodeMeridianUrl = Environment.GetEnvironmentVariable("CodeMeridian_Url") ?? "http://localhost:5100";
var apiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");
bool clear = false;
bool docs = true;
bool watch = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            project = args[++i];
            break;
        case "--CodeMeridian" when i + 1 < args.Length:
            CodeMeridianUrl = args[++i];
            break;
        case "--clear":
            clear = true;
            break;
        case "--no-docs":
            docs = false;
            break;
        case "--watch":
            watch = true;
            break;
        default:
            if (!args[i].StartsWith("--"))
                positional.Add(args[i]);
            break;
    }
}

if (positional.Count == 0)
{
    Console.Error.WriteLine("error: <path> argument is required.");
    PrintUsage();
    return 1;
}

var rootPath = new DirectoryInfo(positional[0]);
if (!rootPath.Exists)
{
    Console.Error.WriteLine($"error: directory not found: {rootPath.FullName}");
    return 1;
}

if (project is null)
{
    project = ResolveProjectName(rootPath);
    Console.WriteLine($"info: --project not specified, resolved to '{project}'");
}

// -- DI setup -----------------------------------------------------------------
var services = new ServiceCollection();

services.AddLogging(b => b
    .AddConsole()
    .SetMinimumLevel(LogLevel.Information));

services.AddCodeMeridianClient(CodeMeridianUrl, apiKey);
services.AddTransient<CSharpIndexer>();
services.AddTransient<DocumentIngester>();
services.AddTransient<IndexerPipeline>();

await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<IndexerPipeline>();

await pipeline.RunAsync(rootPath, project, clear, docs);

if (watch)
{
    var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();
    logger.LogInformation("Watch mode active monitoring {Path} for .cs and .md changes. Press Ctrl+C to exit.", rootPath.FullName);

    // Debounce: collect changes for 2 s of quiet before re-indexing
    var debounceTimer = (System.Timers.Timer?)null;
    var changedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var deletedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

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
            logger.LogInformation("[watch] Change detected re-indexing...");
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
                    includeDocs: docs,
                    changedFiles: changedBatch,
                    deletedFiles: deletedBatch,
                    cancellationToken: cts.Token);
            }
            catch (Exception ex) { logger.LogError(ex, "[watch] Re-index failed."); }
        };
        debounceTimer.Start();
    }

    using var fsWatcher = new FileSystemWatcher(rootPath.FullName)
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        EnableRaisingEvents = true,
    };
    fsWatcher.Filters.Add("*.cs");
    fsWatcher.Filters.Add("*.md");
    fsWatcher.Changed += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
    fsWatcher.Created += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
    fsWatcher.Deleted += (_, e) => ScheduleReindex(e.FullPath, deleted: true);
    fsWatcher.Renamed += (_, e) =>
    {
        ScheduleReindex(e.OldFullPath, deleted: true);
        ScheduleReindex(e.FullPath, deleted: false);
    };

    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
    logger.LogInformation("Watch mode stopped.");
}

return 0;

// -- Help ---------------------------------------------------------------------
static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian Indexer populates the code knowledge graph from a C# codebase

        USAGE:
          CodeMeridian-index <path> [--project <name>] [options]

        ARGUMENTS:
          <path>               Root directory of the project to index

        OPTIONS:
          --project  <name>    Project context name. If omitted, auto-detected from
                               .sln / .slnx / .code-workspace, or the folder name.
          --CodeMeridian <url>     CodeMeridian base URL (default: CodeMeridian_Url or http://localhost:5100)
          --clear              Remove existing knowledge before indexing
          --no-docs            Skip ingestion of .md/.txt files
          --watch              Stay running; re-index when .cs or .md files change
          -h, --help           Show this help
        """);
}

// -- Project name resolution ---------------------------------------------------
static string ResolveProjectName(DirectoryInfo root)
{
    // 1. .sln
    var sln = root.GetFiles("*.sln").FirstOrDefault();
    if (sln is not null) return Path.GetFileNameWithoutExtension(sln.Name);

    // 2. .slnx
    var slnx = root.GetFiles("*.slnx").FirstOrDefault();
    if (slnx is not null) return Path.GetFileNameWithoutExtension(slnx.Name);

    // 3. .code-workspace
    var workspace = root.GetFiles("*.code-workspace").FirstOrDefault();
    if (workspace is not null) return Path.GetFileNameWithoutExtension(workspace.Name);

    // 4. folder name
    return root.Name;
}

static void LoadDotEnv()
{
    var envFile = FindDotEnv(new DirectoryInfo(Directory.GetCurrentDirectory()));
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
