namespace CodeMeridian.Indexer.Cli.Commands;

public static class DocumentIndexingSelection
{
    public static IReadOnlyList<string> FilterDocumentationRelativePaths(
        IEnumerable<string> relativePaths,
        DirectoryInfo rootPath)
    {
        return relativePaths
            .Select(path => NormalizeRelativePath(path, rootPath))
            .Where(path => path is not null && IsDocumentationPath(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static FileInfo[] SelectDocumentationFilesForIndexing(
        IEnumerable<FileInfo> allDocumentFiles,
        DirectoryInfo rootPath,
        IReadOnlyCollection<string>? changedFiles)
    {
        var files = allDocumentFiles.ToArray();
        if (changedFiles is null)
            return files;

        var changedDocs = FilterDocumentationRelativePaths(changedFiles, rootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return files
            .Where(file => changedDocs.Contains(Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/')))
            .ToArray();
    }

    private static string? NormalizeRelativePath(string path, DirectoryInfo rootPath)
    {
        if (!Path.IsPathRooted(path))
            return path.Replace('\\', '/');

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(rootPath.FullName, StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
    }

    private static bool IsDocumentationPath(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
}
