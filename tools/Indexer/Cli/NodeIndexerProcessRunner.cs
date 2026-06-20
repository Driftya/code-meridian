using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class NodeIndexerProcessRunner
{
    public static async Task<int> EnsureDependenciesAsync(DirectoryInfo indexerRoot)
    {
        return await EnsureDependenciesAsync(indexerRoot, RunAsync);
    }

    internal static async Task<int> EnsureDependenciesAsync(
        DirectoryInfo indexerRoot,
        Func<string, IReadOnlyList<string>, DirectoryInfo, IReadOnlyDictionary<string, string?>?, Task<int>> runCommandAsync)
    {
        var localTsx = ResolveTsxCommand(indexerRoot);

        if (localTsx is not null)
            return 0;

        if (!File.Exists(Path.Combine(indexerRoot.FullName, "package.json")))
            return 0;

        Console.WriteLine();
        Console.WriteLine("Node indexer dependencies not found. Restoring npm packages...");

        var npmCommand = ExternalCommandResolver.NpmCommand();
        var packageLockExists = File.Exists(Path.Combine(indexerRoot.FullName, "package-lock.json"));
        if (!packageLockExists)
            return await runCommandAsync(npmCommand, ["install"], indexerRoot, null);

        var installExitCode = await runCommandAsync(npmCommand, ["ci"], indexerRoot, null);
        if (installExitCode == 0)
            return 0;

        Console.WriteLine("npm ci failed. Retrying with npm install to recover from a packaged lockfile mismatch.");
        return await runCommandAsync(npmCommand, ["install"], indexerRoot, null);
    }

    public static async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        DirectoryInfo workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        Console.WriteLine();
        Console.WriteLine($"> {fileName} {string.Join(' ', arguments.Select(QuoteIfNeeded))}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory.FullName,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public static string? ResolveTsxCommand(DirectoryInfo indexerRoot)
    {
        foreach (var searchRoot in EnumerateSearchRoots(indexerRoot))
        {
            var match = EnumerateTsxCandidates(searchRoot).FirstOrDefault(File.Exists);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private static string CombinePath(DirectoryInfo root, params string[] segments)
    {
        var basePath = root.ToString();
        if (LooksLikeWindowsAbsolutePath(basePath))
            return string.Join("\\", new[] { basePath.TrimEnd('\\', '/') }.Concat(segments));

        return Path.Combine(new[] { root.FullName }.Concat(segments).ToArray());
    }

    private static IEnumerable<DirectoryInfo> EnumerateSearchRoots(DirectoryInfo indexerRoot)
    {
        for (var current = indexerRoot; current is not null; current = current.Parent)
            yield return current;
    }

    private static IEnumerable<string> EnumerateTsxCandidates(DirectoryInfo root)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return CombinePath(root, "node_modules", ".bin", "tsx.cmd");
            yield return CombinePath(root, "node_modules", ".bin", "tsx");
            yield break;
        }

        yield return CombinePath(root, "node_modules", ".bin", "tsx");
        yield return CombinePath(root, "node_modules", ".bin", "tsx.cmd");
    }

    private static bool LooksLikeWindowsAbsolutePath(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
