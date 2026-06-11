using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class InitCommand(
    IToolConfigurationService configurationService,
    CodeMeridianConfigFileStore configFileStore,
    IProjectDiscoveryService projectDiscoveryService,
    ServeWriter serveWriter)
{
    public int Run(InitCommandOptions options)
    {
        if (options.Global)
            return RunGlobal(options);

        var context = configurationService.CreateContext(options.Path);
        var rootPath = context.RootPath;
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);

        Directory.CreateDirectory(rootPath.FullName);

        var project = NormalizeOptionalString(options.Project)
            ?? context.LocalConfig?.Project
            ?? context.GlobalConfig?.Project
            ?? projectDiscoveryService.ResolveProjectName(rootPath);

        try
        {
            configFileStore.Write(
                rootPath,
                project,
                codeMeridianUrl,
                useGlobalCache: false,
                overwrite: options.Force);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        var configPath = Path.Combine(rootPath.FullName, "meridian.json");
        var clientConfigChanges = serveWriter.ApplyClientConfig(rootPath, codeMeridianUrl, options.Force);

        Console.WriteLine("Initialized CodeMeridian indexer config:");
        Console.WriteLine($"  Path    : {configPath}");
        Console.WriteLine($"  Project : {project}");
        Console.WriteLine($"  Server  : {codeMeridianUrl}");
        Console.WriteLine();
        Console.WriteLine("Client MCP config:");
        foreach (var change in clientConfigChanges)
            Console.WriteLine($"  {change.Status,-11} {change.Path}");
        Console.WriteLine();
        Console.WriteLine("Next step:");
        Console.WriteLine("  codemeridian index .");

        return 0;
    }

    public int RunGlobal(InitCommandOptions options, DirectoryInfo? globalConfigDirectory = null)
    {
        var context = configurationService.CreateContext(options.Path);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);

        try
        {
            configFileStore.WriteGlobal(
                codeMeridianUrl,
                overwrite: options.Force,
                globalConfigDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        var configPath = configFileStore.GetGlobalConfigFile(globalConfigDirectory).FullName;

        Console.WriteLine("Initialized global CodeMeridian indexer config:");
        Console.WriteLine($"  Path   : {configPath}");
        Console.WriteLine($"  Server : {codeMeridianUrl}");
        Console.WriteLine();
        Console.WriteLine("Runtime cache and generated files will be stored outside the repository.");
        Console.WriteLine();
        Console.WriteLine("Next step:");
        Console.WriteLine("  codemeridian index .");

        return 0;
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
