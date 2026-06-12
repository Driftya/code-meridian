namespace CodeMeridian.Indexer.Cli.Commands;

internal static class TypeScriptIndexerCommandBuilder
{
    public static List<string> BuildTypeScriptIndexerArgs(DirectoryInfo tsIndexerRoot, DirectoryInfo rootPath, string project)
    {
        return
        [
            Path.Combine(tsIndexerRoot.FullName, "src", "index.ts"),
            rootPath.FullName,
            "--project",
            project,
        ];
    }

    public static void AddTypeScriptIndexerOptions(
        List<string> arguments,
        string codeMeridianUrl,
        bool watch,
        bool clear,
        bool includeDocs,
        FileInfo? filesList)
    {
        arguments.AddRange(["--url", codeMeridianUrl]);
        if (clear)
            arguments.Add("--clear");
        if (!includeDocs)
            arguments.Add("--no-docs");
        if (watch)
            arguments.Add("--watch");
        if (filesList is not null)
            arguments.AddRange(["--files-list", filesList.FullName]);
    }
}
