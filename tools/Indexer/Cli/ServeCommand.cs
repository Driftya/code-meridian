using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli;

internal static class ServeCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs)
    {
        var positional = new List<string>();
        var host = ServeOptions.DefaultHost;
        var port = ServeOptions.DefaultPort;
        var neo4jHttpPort = ServeOptions.DefaultNeo4jHttpPort;
        var neo4jBoltPort = ServeOptions.DefaultNeo4jBoltPort;
        var composeFile = ServeOptions.DefaultComposeFileName;
        var image = ServeOptions.DefaultImage;
        var force = false;
        var start = true;

        for (var i = 0; i < rawArgs.Count; i++)
        {
            switch (rawArgs[i])
            {
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                case "--host" when i + 1 < rawArgs.Count:
                    host = rawArgs[++i];
                    break;
                case "--port" when i + 1 < rawArgs.Count && int.TryParse(rawArgs[i + 1], out var parsedPort):
                    port = parsedPort;
                    i++;
                    break;
                case "--neo4j-http-port" when i + 1 < rawArgs.Count && int.TryParse(rawArgs[i + 1], out var parsedHttpPort):
                    neo4jHttpPort = parsedHttpPort;
                    i++;
                    break;
                case "--neo4j-bolt-port" when i + 1 < rawArgs.Count && int.TryParse(rawArgs[i + 1], out var parsedBoltPort):
                    neo4jBoltPort = parsedBoltPort;
                    i++;
                    break;
                case "--compose-file" when i + 1 < rawArgs.Count:
                    composeFile = rawArgs[++i];
                    break;
                case "--image" when i + 1 < rawArgs.Count:
                    image = rawArgs[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                case "--no-start":
                    start = false;
                    break;
                default:
                    if (!rawArgs[i].StartsWith("-", StringComparison.Ordinal))
                        positional.Add(rawArgs[i]);
                    else
                        Console.Error.WriteLine($"warn: unknown option ignored: {rawArgs[i]}");
                    break;
            }
        }

        var rootPath = new DirectoryInfo(positional.Count > 0
            ? Path.GetFullPath(positional[0], Directory.GetCurrentDirectory())
            : Directory.GetCurrentDirectory());

        var options = new ServeOptions(
            rootPath,
            host,
            port,
            neo4jHttpPort,
            neo4jBoltPort,
            composeFile,
            image,
            force,
            start);

        ServeApplyResult result;
        try
        {
            result = new ServeWriter().Apply(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        Console.WriteLine("CodeMeridian serve");
        Console.WriteLine($"  Root    : {rootPath.FullName}");
        Console.WriteLine($"  Server  : http://{host}:{port}");
        Console.WriteLine($"  Compose : {result.ComposePath}");
        Console.WriteLine();
        Console.WriteLine("Files:");
        foreach (var change in result.Changes)
            Console.WriteLine($"  {change.Status,-11} {change.Path}");

        if (!start)
        {
            Console.WriteLine();
            Console.WriteLine("Next step:");
            Console.WriteLine($"  docker compose -f {QuoteIfNeeded(result.ComposePath)} up -d");
            return 0;
        }

        return await RunProcessAsync("docker", ["compose", "-f", result.ComposePath, "up", "-d"], rootPath);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            CodeMeridian Serve - create local MCP client config and start the backend stack.

            USAGE:
              codemeridian serve [path] [options]

            OPTIONS:
              --host <host>                 Hostname for generated MCP URLs. Default: localhost.
              --port <port>                 MCP server host port. Default: 5100.
              --neo4j-http-port <port>      Neo4j browser host port. Default: 47474.
              --neo4j-bolt-port <port>      Neo4j bolt host port. Default: 47687.
              --image <image>               MCP server image. Default: ghcr.io/driftya/codemeridian-mcp:latest.
              --compose-file <path>         Compose file to create/use. Default: docker-compose.codemeridian.yml.
              --force                       Back up and overwrite generated files where needed.
              --no-start                    Write files but do not run docker compose.
              -h, --help                    Show this help.

            EXAMPLES:
              codemeridian serve
              codemeridian serve --no-start
              codemeridian serve --host localhost --port 5100
              codemeridian serve --image ghcr.io/driftya/codemeridian-mcp:latest
            """);
    }

    private static async Task<int> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, DirectoryInfo workingDirectory)
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

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
