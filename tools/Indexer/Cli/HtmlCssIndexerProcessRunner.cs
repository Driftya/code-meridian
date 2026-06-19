namespace CodeMeridian.Indexer.Cli.Commands;

internal static class HtmlCssIndexerProcessRunner
{
    public static async Task<int> EnsureDependenciesAsync(DirectoryInfo htmlCssIndexerRoot)
    {
        return await NodeIndexerProcessRunner.EnsureDependenciesAsync(htmlCssIndexerRoot);
    }

    public static async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        DirectoryInfo workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        return await NodeIndexerProcessRunner.RunAsync(fileName, arguments, workingDirectory, environmentVariables);
    }

    public static string? ResolveTsxCommand(DirectoryInfo htmlCssIndexerRoot)
    {
        return NodeIndexerProcessRunner.ResolveTsxCommand(htmlCssIndexerRoot);
    }
}
