using System.Diagnostics;
using CodeMeridian.Indexer.Cli;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ServeCommand(ServeWriter serveWriter)
{
    public async Task<int> RunAsync(ServeOptions options)
    {
        ServeApplyResult result;
        try
        {
            result = serveWriter.Apply(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        Console.WriteLine("CodeMeridian serve");
        Console.WriteLine($"  Root    : {options.RootDirectory.FullName}");
        Console.WriteLine($"  Server  : http://{options.Host}:{options.Port}");
        Console.WriteLine($"  Compose : {result.ComposePath}");
        Console.WriteLine();
        Console.WriteLine("Files:");
        foreach (var change in result.Changes)
            Console.WriteLine($"  {change.Status,-11} {change.Path}");

        if (!options.Start)
        {
            Console.WriteLine();
            Console.WriteLine("Next step:");
            foreach (var step in BuildDockerCommands(result.ComposePath))
                Console.WriteLine($"  {FormatCommand(step)}");
            return 0;
        }

        foreach (var step in BuildDockerCommands(result.ComposePath))
        {
            var exitCode = await RunProcessAsync("docker", step, options.RootDirectory);
            if (exitCode != 0)
                return exitCode;
        }

        return 0;
    }

    internal static IReadOnlyList<IReadOnlyList<string>> BuildDockerCommands(string composePath) =>
    [
        ["compose", "-f", composePath, "pull"],
        ["compose", "-f", composePath, "up", "-d"],
    ];

    private static async Task<int> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, DirectoryInfo workingDirectory)
    {
        Console.WriteLine();
        Console.WriteLine($"> {FormatCommand(arguments, fileName)}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory.FullName,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    internal static string FormatCommand(IReadOnlyList<string> arguments, string fileName = "docker") =>
        $"{fileName} {string.Join(' ', arguments.Select(QuoteIfNeeded))}";

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
