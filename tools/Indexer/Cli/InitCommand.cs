using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class InitCommand(
    IToolConfigurationService configurationService,
    CodeMeridianConfigFileStore configFileStore,
    IProjectDiscoveryService projectDiscoveryService,
    ServeWriter serveWriter,
    IInitPromptService promptService)
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
            var clientConfigTargets = new[]
            {
                new InitTarget(".vscode/mcp.json", Path.Combine(rootPath.FullName, ".vscode", "mcp.json")),
                new InitTarget(".continue/mcpServers/code-meridian.yaml", Path.Combine(rootPath.FullName, ".continue", "mcpServers", "code-meridian.yaml")),
                new InitTarget(".codex/config.toml", Path.Combine(rootPath.FullName, ".codex", "config.toml")),
            };

            var selectedClientConfigs = promptService.ReadSelections(
                "Select client configs to create or merge:",
                clientConfigTargets.Select(target => new PromptSelection(target.DisplayName, !File.Exists(target.FullPath))).ToArray());
            var includeAgentCapabilities = promptService.ReadYesNo(
                "Include meridian-agent-capabilities?",
                defaultAnswer: !Directory.Exists(Path.Combine(rootPath.FullName, "meridian-agent-capabilities")));
            var clientConfigChanges = selectedClientConfigs.Count == 0
                ? []
                : serveWriter.ApplyClientConfig(rootPath, codeMeridianUrl, options.Force, selectedClientConfigs);

            if (includeAgentCapabilities)
                configFileStore.WriteAgentCapabilities(rootPath, options.Force);

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
            Console.WriteLine($"  Client configs        : {(selectedClientConfigs.Count > 0 ? "included" : "skipped")}");
            Console.WriteLine($"  Agent capabilities    : {(includeAgentCapabilities ? "included" : "skipped")}");
            Console.WriteLine();
            if (selectedClientConfigs.Count > 0)
            {
                Console.WriteLine("Client MCP config:");
                foreach (var change in clientConfigChanges)
                    Console.WriteLine($"  {change.Status,-11} {change.Path}");
                Console.WriteLine();
            }
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
            var includeAgentCapabilities = promptService.ReadYesNo(
                "Include meridian-agent-capabilities?",
                defaultAnswer: !Directory.Exists(Path.Combine((globalConfigDirectory ?? configFileStore.GetGlobalConfigDirectory()).FullName, "meridian-agent-capabilities")));

            if (includeAgentCapabilities)
                configFileStore.WriteAgentCapabilities(globalConfigDirectory ?? configFileStore.GetGlobalConfigDirectory(), options.Force);

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
            Console.WriteLine($"  Agent capabilities    : {(includeAgentCapabilities ? "included" : "skipped")}");
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

public interface IInitPromptService
{
    bool ReadYesNo(string message, bool defaultAnswer);
    IReadOnlyList<string> ReadSelections(string message, IReadOnlyList<PromptSelection> selections);
}

public sealed class ConsoleInitPromptService : IInitPromptService
{
    public bool ReadYesNo(string message, bool defaultAnswer)
    {
        var suffix = defaultAnswer ? "[Y/n]" : "[y/N]";

        while (true)
        {
            Console.Write($"{message} {suffix} ");
            var choice = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(choice))
                return defaultAnswer;

            switch (choice.Trim().ToLowerInvariant())
            {
                case "y":
                case "yes":
                    return true;
                case "n":
                case "no":
                    return false;
            }

            Console.WriteLine("Please answer y or n.");
        }
    }

    public IReadOnlyList<string> ReadSelections(string message, IReadOnlyList<PromptSelection> selections)
    {
        Console.WriteLine(message);
        var chosen = new List<string>();
        for (var i = 0; i < selections.Count; i++)
        {
            var selection = selections[i];
            var suffix = selection.DefaultSelected ? "[Y/n]" : "[y/N]";
            Console.Write($"{i + 1}. {selection.Label} {suffix} ");
            var choice = Console.ReadLine();

            var selected = string.IsNullOrWhiteSpace(choice)
                ? selection.DefaultSelected
                : IsYes(choice);

            if (selected)
            {
                chosen.Add(selection.Label);
                Console.WriteLine("  selected");
            }
        }

        return chosen;
    }

    private static bool IsYes(string value) =>
        value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
        value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
}

public sealed record PromptSelection(string Label, bool DefaultSelected);

internal sealed record InitTarget(string DisplayName, string FullPath);
