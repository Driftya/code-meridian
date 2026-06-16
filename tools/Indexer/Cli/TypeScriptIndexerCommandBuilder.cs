namespace CodeMeridian.Indexer.Cli.Commands;

internal static class TypeScriptIndexerCommandBuilder
{
    public static List<string> BuildTypeScriptIndexerArgs(DirectoryInfo tsIndexerRoot, DirectoryInfo rootPath, string project)
    {
        return
        [
            CombinePath(tsIndexerRoot, "src", "index.ts"),
            GetPath(rootPath),
            "--project",
            project,
        ];
    }

    public static void AddTypeScriptIndexerOptions(
        List<string> arguments,
        string codeMeridianUrl,
        FileInfo batchFile)
    {
        arguments.AddRange(["--url", codeMeridianUrl]);
        arguments.AddRange(["--batch-file", GetPath(batchFile)]);
    }

    private static string CombinePath(DirectoryInfo root, params string[] segments)
    {
        var basePath = GetPath(root);
        if (LooksLikeWindowsAbsolutePath(basePath))
            return string.Join("\\", new[] { basePath.TrimEnd('\\', '/') }.Concat(segments));

        return Path.Combine(new[] { root.FullName }.Concat(segments).ToArray());
    }

    private static string GetPath(FileSystemInfo path)
    {
        var original = path.ToString();
        return LooksLikeWindowsAbsolutePath(original) ? original : path.FullName;
    }

    private static bool LooksLikeWindowsAbsolutePath(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
