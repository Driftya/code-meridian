using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class IndexExecutionPlanBuilder
{
    public static IncrementalIndexPlan BuildPlan(
        IncrementalIndexCache cache,
        DirectoryInfo rootPath,
        IReadOnlyList<FileInfo> indexableFiles,
        bool forceFull) =>
        cache.BuildPlan(rootPath, indexableFiles, forceFull);

    public static IReadOnlyCollection<string> GetChangedFiles(
        IncrementalIndexPlan incrementalPlan,
        bool incremental,
        bool clear) =>
        incremental && !clear ? incrementalPlan.ChangedFiles : [];

    public static IReadOnlyCollection<string> GetDeletedFiles(
        IncrementalIndexPlan incrementalPlan,
        bool incremental,
        bool clear) =>
        incremental && !clear ? incrementalPlan.DeletedFiles : [];

    public static IReadOnlyList<FileInfo> EnumerateIndexableFiles(
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
                (includeDocs && IsDocumentationFile(file)))
            .ToArray();
    }

    public static bool IsCSharpSourceFile(FileInfo file) =>
        file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public static bool IsTypeScriptSourceFile(FileInfo file) =>
        (file.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
         file.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) &&
        !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

    public static bool IsDocumentationFile(FileInfo file) =>
        file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("ARCHITECTURE.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase);

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
}
