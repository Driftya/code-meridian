namespace CodeMeridian.Indexer.Cli.Commands;

internal static class HtmlCssIndexerCommandBuilder
{
    public static List<string> BuildHtmlCssIndexerArgs(DirectoryInfo htmlCssIndexerRoot, DirectoryInfo rootPath, string project)
    {
        return
        [
            CombinePath(htmlCssIndexerRoot, "src", "index.ts"),
            GetPath(rootPath),
            "--project",
            project,
        ];
    }

    public static void AddHtmlCssIndexerOptions(
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
