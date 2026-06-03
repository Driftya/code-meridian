using System.Diagnostics;

var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
if (repoRoot is null)
{
    Console.Error.WriteLine("error: could not locate CodeMeridian repository root.");
    return 1;
}

var positional = new List<string>();
string? project = null;
string? codeMeridianUrl = null;
var clear = false;
var docs = true;
var watch = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
        case "--project" when i + 1 < args.Length:
            project = args[++i];
            break;
        case "--CodeMeridian" when i + 1 < args.Length:
        case "--url" when i + 1 < args.Length:
            codeMeridianUrl = args[++i];
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
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                positional.Add(args[i]);
            break;
    }
}

var rootPath = new DirectoryInfo(positional.Count > 0
    ? Path.GetFullPath(positional[0], Directory.GetCurrentDirectory())
    : Directory.GetCurrentDirectory());

if (!rootPath.Exists)
{
    Console.Error.WriteLine($"error: directory not found: {rootPath.FullName}");
    return 1;
}

project ??= ResolveProjectName(rootPath);
Console.WriteLine($"info: indexing root: {rootPath.FullName}");
Console.WriteLine($"info: project: {project}");

var hasCSharp = ContainsFile(rootPath, ".cs");
var typeScriptRoots = FindTypeScriptRoots(rootPath);
var hasTypeScript = typeScriptRoots.Count > 0;

if (!hasCSharp && !hasTypeScript)
{
    Console.WriteLine("No .cs, .ts, or .tsx files found. Nothing to index.");
    return 0;
}

var exitCode = 0;
var clearNextIndexer = clear;

if (hasCSharp)
{
    var csharpArgs = new List<string>
    {
        "run",
        "--project",
        Path.Combine(repoRoot.FullName, "tools", "Indexer"),
        "--",
        rootPath.FullName,
        "--project",
        project,
    };

    if (codeMeridianUrl is not null)
        csharpArgs.AddRange(["--CodeMeridian", codeMeridianUrl]);
    if (clearNextIndexer)
        csharpArgs.Add("--clear");
    if (!docs)
        csharpArgs.Add("--no-docs");
    if (watch)
        csharpArgs.Add("--watch");

    exitCode = await RunAsync(DotnetCommand(), csharpArgs, repoRoot);
    if (exitCode != 0 || watch)
        return exitCode;

    clearNextIndexer = false;
}

if (hasTypeScript)
{
    var tsIndexerRoot = new DirectoryInfo(Path.Combine(repoRoot.FullName, "tools", "TsIndexer"));
    var tsxCommand = ResolveTsxCommand(tsIndexerRoot);
    foreach (var typeScriptRoot in typeScriptRoots)
    {
        var tsArgs = new List<string>
        {
            Path.Combine(repoRoot.FullName, "tools", "TsIndexer", "src", "index.ts"),
            typeScriptRoot.FullName,
            "--project",
            project,
        };

        if (codeMeridianUrl is not null)
            tsArgs.AddRange(["--url", codeMeridianUrl]);
        if (clearNextIndexer)
            tsArgs.Add("--clear");
        if (!docs || hasCSharp || typeScriptRoots.Count > 1)
            tsArgs.Add("--no-docs");
        if (watch)
            tsArgs.Add("--watch");

        if (tsxCommand.UseNpx)
            tsArgs.Insert(0, "tsx");

        exitCode = await RunAsync(tsxCommand.FileName, tsArgs, tsIndexerRoot);
        if (exitCode != 0 || watch)
            return exitCode;

        clearNextIndexer = false;
    }
}

return exitCode;

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

static DirectoryInfo? FindRepositoryRoot(string startPath)
{
    for (var current = new DirectoryInfo(startPath); current is not null; current = current.Parent)
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

static string QuoteIfNeeded(string value)
{
    return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

static string DotnetCommand() => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

static string NpxCommand() => OperatingSystem.IsWindows() ? "npx.cmd" : "npx";

static (string FileName, bool UseNpx) ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
{
    var localTsx = Path.Combine(
        tsIndexerRoot.FullName,
        "node_modules",
        ".bin",
        OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

    return File.Exists(localTsx) ? (localTsx, false) : (NpxCommand(), true);
}

static void PrintUsage()
{
    Console.WriteLine("""
        CodeMeridian IndexerAll - runs the C# and TypeScript indexers when matching files exist.

        USAGE:
          dotnet run --project tools/IndexerAll -- [path] [--project <name>] [options]

        ARGUMENTS:
          [path]              Root directory to scan. Defaults to the shell's current directory.

        OPTIONS:
          --project <name>    Project context name. If omitted, auto-detected from the target root.
          --url <url>         CodeMeridian server URL. Alias for --CodeMeridian.
          --CodeMeridian <url> CodeMeridian server URL.
          --clear             Remove existing knowledge before indexing. Applied only once.
          --no-docs           Skip documentation ingestion.
          --watch             Watch mode. If both languages are present, C# watch runs first.
          -h, --help          Show this help.
        """);
}
