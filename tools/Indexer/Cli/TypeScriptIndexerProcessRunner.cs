namespace CodeMeridian.Indexer.Cli.Commands;

internal static class TypeScriptIndexerProcessRunner
{
    public static async Task<int> EnsureDependenciesAsync(DirectoryInfo tsIndexerRoot)
    {
        return await EnsureDependenciesAsync(tsIndexerRoot, NodeIndexerProcessRunner.RunAsync);
    }

    public static async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        DirectoryInfo workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        return await NodeIndexerProcessRunner.RunAsync(fileName, arguments, workingDirectory, environmentVariables);
    }

    public static string? ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
    {
        return NodeIndexerProcessRunner.ResolveTsxCommand(tsIndexerRoot);
    }

    internal static async Task<int> EnsureDependenciesAsync(
        DirectoryInfo tsIndexerRoot,
        Func<string, IReadOnlyList<string>, DirectoryInfo, IReadOnlyDictionary<string, string?>?, Task<int>> runCommandAsync)
    {
        return await NodeIndexerProcessRunner.EnsureDependenciesAsync(tsIndexerRoot, runCommandAsync);
    }
}
