using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class TypeScriptIndexerProcessRunner
{
    public static async Task<int> EnsureDependenciesAsync(DirectoryInfo tsIndexerRoot)
    {
        return await EnsureDependenciesAsync(tsIndexerRoot, RunAsync);
    }

    internal static async Task<int> EnsureDependenciesAsync(
        DirectoryInfo tsIndexerRoot,
        Func<string, IReadOnlyList<string>, DirectoryInfo, IReadOnlyDictionary<string, string?>?, Task<int>> runCommandAsync)
    {
        var localTsx = ResolveTsxCommand(tsIndexerRoot);

        if (localTsx is not null)
            return 0;

        if (!File.Exists(Path.Combine(tsIndexerRoot.FullName, "package.json")))
            return 0;

        Console.WriteLine();
        Console.WriteLine("TypeScript indexer dependencies not found. Restoring npm packages...");

        var npmCommand = ExternalCommandResolver.NpmCommand();
        var packageLockExists = File.Exists(Path.Combine(tsIndexerRoot.FullName, "package-lock.json"));
        if (!packageLockExists)
            return await runCommandAsync(npmCommand, ["install"], tsIndexerRoot, null);

        var installExitCode = await runCommandAsync(npmCommand, ["ci"], tsIndexerRoot, null);
        if (installExitCode == 0)
            return 0;

        Console.WriteLine("npm ci failed. Retrying with npm install to recover from a packaged lockfile mismatch.");
        return await runCommandAsync(npmCommand, ["install"], tsIndexerRoot, null);
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

    public static string? ResolveTsxCommand(DirectoryInfo tsIndexerRoot)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                CombinePath(tsIndexerRoot, "node_modules", ".bin", "tsx.cmd"),
                CombinePath(tsIndexerRoot, "node_modules", ".bin", "tsx")
            }
            : new[]
            {
                CombinePath(tsIndexerRoot, "node_modules", ".bin", "tsx"),
                CombinePath(tsIndexerRoot, "node_modules", ".bin", "tsx.cmd")
            };

        return candidates.FirstOrDefault(File.Exists);
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

    private static bool LooksLikeWindowsAbsolutePath(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
