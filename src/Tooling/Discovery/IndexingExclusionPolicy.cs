namespace CodeMeridian.Tooling.Discovery;

public static class IndexingExclusionPolicy
{
    private static readonly string[] IgnoredDirectoryNames =
    [
        ".git",
        ".vs",
        ".vscode",
        ".meridian",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "coverage"
    ];

    public static bool IsIgnoredDirectoryName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        IgnoredDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsIgnoredPath(DirectoryInfo rootPath, FileSystemInfo path) =>
        IsIgnoredRelativePath(Path.GetRelativePath(rootPath.FullName, path.FullName));

    public static bool IsIgnoredRelativePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(IsIgnoredDirectoryName) ||
               normalizedPath.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }
}
