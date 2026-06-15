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
        FileInfo batchFile)
    {
        arguments.AddRange(["--url", codeMeridianUrl]);
        arguments.AddRange(["--batch-file", batchFile.FullName]);
    }
}
