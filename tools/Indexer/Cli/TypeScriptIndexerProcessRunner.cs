using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class TypeScriptIndexerProcessRunner
{
    public static async Task<int> EnsureDependenciesAsync(DirectoryInfo tsIndexerRoot)
    {
        var localTsx = ResolveTsxCommand(tsIndexerRoot);

        if (localTsx is not null)
            return 0;

        if (!File.Exists(Path.Combine(tsIndexerRoot.FullName, "package.json")))
            return 0;

        Console.WriteLine();
        Console.WriteLine("TypeScript indexer dependencies not found. Restoring npm packages...");

        var packageLock = Path.Combine(tsIndexerRoot.FullName, "package-lock.json");
        var arguments = File.Exists(packageLock)
            ? new[] { "ci", "--silent" }
            : ["install", "--silent"];

        return await RunAsync(ExternalCommandResolver.NpmCommand(), arguments, tsIndexerRoot);
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
        var localTsx = Path.Combine(
            tsIndexerRoot.FullName,
            "node_modules",
            ".bin",
            OperatingSystem.IsWindows() ? "tsx.cmd" : "tsx");

        return File.Exists(localTsx) ? localTsx : null;
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
