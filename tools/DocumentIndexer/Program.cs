using CodeMeridian.DocumentIndexer.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddTransient<DocumentIndexerPipeline>();

await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<DocumentIndexerPipeline>();

if (args.Length == 0)
{
    Console.WriteLine("Usage: codemeridian-docs <root-path> [project-name]");
    return;
}

var rootPath = new DirectoryInfo(Path.GetFullPath(args[0]));
var projectName = args.Length > 1 ? args[1] : rootPath.Name;
var files = EnumerateDocumentFiles(rootPath)
    .ToArray();

await pipeline.IngestAsync(files, projectName, rootPath.FullName, CancellationToken.None);

static IEnumerable<FileInfo> EnumerateDocumentFiles(DirectoryInfo rootPath)
{
    var pending = new Stack<DirectoryInfo>();
    pending.Push(rootPath);

    while (pending.Count > 0)
    {
        var current = pending.Pop();
        if (ShouldSkipDirectory(current))
            continue;

        foreach (var file in SafeEnumerateFiles(current))
        {
            if (IsDocumentFile(file))
                yield return file;
        }

        foreach (var directory in SafeEnumerateDirectories(current))
            pending.Push(directory);
    }
}

static bool IsDocumentFile(FileInfo file) =>
    file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
    file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("ARCHITECTURE.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
    file.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase);

static bool ShouldSkipDirectory(DirectoryInfo directory)
{
    var name = directory.Name;
    return name is ".git" or ".vs" or ".vscode" or ".meridian" or "bin" or "obj" or "node_modules" or "dist" or "build" or "coverage";
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
