using CodeMeridian.Tooling.Storage;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class IndexExecutionPlanBuilder
{
    public static IncrementalIndexPlan BuildPlan(
        IncrementalIndexCache cache,
        DirectoryInfo rootPath,
        IReadOnlyList<FileInfo> indexableFiles,
        bool forceFull,
        Func<string, bool>? isPathInScope = null) =>
        cache.BuildPlan(rootPath, indexableFiles, forceFull, isPathInScope);

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
        bool includeDocs,
        bool includeConfiguration = false,
        IReadOnlyList<string>? configurationFilePatterns = null)
    {
        return rootPath
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredPath(rootPath, file))
            .Where(file => IsIndexableFile(
                file,
                includeCSharp,
                includeTypeScript,
                includeDocs,
                includeConfiguration,
                configurationFilePatterns))
            .ToArray();
    }

    public static bool IsIndexableFile(
        FileInfo file,
        bool includeCSharp,
        bool includeTypeScript,
        bool includeDocs,
        bool includeConfiguration,
        IReadOnlyList<string>? configurationFilePatterns = null) =>
        (includeCSharp && IsCSharpSourceFile(file)) ||
        (includeTypeScript && IsTypeScriptSourceFile(file)) ||
        (includeTypeScript && IsHtmlCssSourceFile(file)) ||
        (includeDocs && IsDocumentationFile(file)) ||
        (includeConfiguration && Configuration.ConfigurationFilePatternMatcher.IsConfigurationFile(file, configurationFilePatterns));

    public static bool IsCSharpSourceFile(FileInfo file) =>
        file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public static bool IsTypeScriptSourceFile(FileInfo file) =>
        (file.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
         file.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) &&
        !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

    public static bool IsHtmlCssSourceFile(FileInfo file) =>
        file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".scss", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase);

    public static bool IsDocumentationFile(FileInfo file) =>
        file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("ARCHITECTURE.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
        file.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase);

    public static bool IsConfigurationFile(FileInfo file) =>
        Configuration.ConfigurationFilePatternMatcher.IsConfigurationFile(file);

    internal static bool IsIgnoredPath(DirectoryInfo rootPath, FileInfo file)
        => IndexingExclusionPolicy.IsIgnoredPath(rootPath, file);
}
