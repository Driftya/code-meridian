using CodeMeridian.Indexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── Parse args ────────────────────────────────────────────────────────────────
if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var positional = new List<string>();
string? project = null;
string CodeMeridianUrl = "http://localhost:5100";
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

// ── DI setup ─────────────────────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b => b
    .AddConsole()
    .SetMinimumLevel(LogLevel.Information));

services.AddCodeMeridianClient(CodeMeridianUrl);
services.AddTransient<CSharpIndexer>();
services.AddTransient<DocumentIngester>();
services.AddTransient<IndexerPipeline>();

await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<IndexerPipeline>();

await pipeline.RunAsync(rootPath, project, clear, docs);

if (watch)
{
    var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();
    logger.LogInformation("Watch mode active — monitoring {Path} for .cs and .md changes. Press Ctrl+C to exit.", rootPath.FullName);

    // Debounce: collect changes for 2 s of quiet before re-indexing
    var debounceTimer = (System.Timers.Timer?)null;
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    void ScheduleReindex()
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();
        debounceTimer = new System.Timers.Timer(2_000) { AutoReset = false };
        debounceTimer.Elapsed += async (_, _) =>
        {
            logger.LogInformation("[watch] Change detected — re-indexing...");
            try { await pipeline.RunAsync(rootPath, project, clear: false, docs, cts.Token); }
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
    fsWatcher.Changed += (_, _) => ScheduleReindex();
    fsWatcher.Created += (_, _) => ScheduleReindex();
    fsWatcher.Deleted += (_, _) => ScheduleReindex();
    fsWatcher.Renamed += (_, _) => ScheduleReindex();

    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
    logger.LogInformation("Watch mode stopped.");
}

return 0;

// ── Help ─────────────────────────────────────────────────────────────────────
static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian Indexer — populates the code knowledge graph from a C# codebase

        USAGE:
          CodeMeridian-index <path> [--project <name>] [options]

        ARGUMENTS:
          <path>               Root directory of the project to index

        OPTIONS:
          --project  <name>    Project context name. If omitted, auto-detected from
                               .sln / .slnx / .code-workspace, or the folder name.
          --CodeMeridian <url>     CodeMeridian base URL (default: http://localhost:5100)
          --clear              Remove existing knowledge before indexing
          --no-docs            Skip ingestion of .md/.txt files
          --watch              Stay running; re-index when .cs or .md files change
          -h, --help           Show this help
        """);
}

// ── Project name resolution ───────────────────────────────────────────────────
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
