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
            var writeResult = configFileStore.Write(
                rootPath,
                project,
                codeMeridianUrl,
                useGlobalCache: false,
                overwrite: options.Force);
            var configPath = Path.Combine(rootPath.FullName, "meridian.json");
            var selectedArchitecturePath = context.LocalConfig?.ArchitecturePath
                ?? context.GlobalConfig?.ArchitecturePath
                ?? CodeMeridianConfigFileStore.DefaultArchitecturePath;
            var availableTemplates = string.Join(", ",
                configFileStore.GetArchitectureTemplateFileNames()
                    .Select(fileName => $".meridian/architectures/{fileName}"));
            var clientConfigChanges = serveWriter.ApplyClientConfig(rootPath, codeMeridianUrl, options.Force);

            Console.WriteLine(writeResult.Created
                ? "Initialized CodeMeridian indexer config:"
                : writeResult.Changed
                    ? "Refreshed CodeMeridian indexer config:"
                    : "CodeMeridian indexer config is already current:");
            Console.WriteLine($"  Path    : {configPath}");
            Console.WriteLine($"  Project : {project}");
            Console.WriteLine($"  Server  : {codeMeridianUrl}");
            Console.WriteLine($"  Version : {writeResult.CurrentVersion}");
            if (!writeResult.Created && writeResult.BackupPath is not null)
                Console.WriteLine($"  Backup  : {writeResult.BackupPath}");
            if (writeResult.AddedPaths.Count > 0)
                Console.WriteLine($"  Added   : {string.Join(", ", writeResult.AddedPaths)}");
            Console.WriteLine($"  Architecture selected  : {selectedArchitecturePath}");
            Console.WriteLine($"  Architecture templates : {availableTemplates}");
            Console.WriteLine();
            Console.WriteLine("Client MCP config:");
            foreach (var change in clientConfigChanges)
                Console.WriteLine($"  {change.Status,-11} {change.Path}");
            Console.WriteLine();
            Console.WriteLine("Next step:");
            Console.WriteLine("  codemeridian index .");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    public int RunGlobal(InitCommandOptions options, DirectoryInfo? globalConfigDirectory = null)
    {
        var context = configurationService.CreateContext(options.Path);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);

        try
        {
            var writeResult = configFileStore.WriteGlobal(
                codeMeridianUrl,
                overwrite: options.Force,
                globalConfigDirectory);
            var configPath = configFileStore.GetGlobalConfigFile(globalConfigDirectory).FullName;

            Console.WriteLine(writeResult.Created
                ? "Initialized global CodeMeridian indexer config:"
                : writeResult.Changed
                    ? "Refreshed global CodeMeridian indexer config:"
                    : "Global CodeMeridian indexer config is already current:");
            Console.WriteLine($"  Path    : {configPath}");
            Console.WriteLine($"  Server  : {codeMeridianUrl}");
            Console.WriteLine($"  Version : {writeResult.CurrentVersion}");
            if (writeResult.BackupPath is not null)
                Console.WriteLine($"  Backup  : {writeResult.BackupPath}");
            if (writeResult.AddedPaths.Count > 0)
                Console.WriteLine($"  Added   : {string.Join(", ", writeResult.AddedPaths)}");
            Console.WriteLine();
            Console.WriteLine("Runtime cache and generated files will be stored outside the repository.");
            Console.WriteLine();
            Console.WriteLine("Next step:");
            Console.WriteLine("  codemeridian index .");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
