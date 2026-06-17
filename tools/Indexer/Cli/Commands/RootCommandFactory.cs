using System.CommandLine;
using CodeMeridian.Indexer.Cli;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class RootCommandFactory(
    IServiceProvider services,
    IToolConfigurationService configurationService,
    IndexCommandSettingsFactory settingsFactory,
    InitCommand initCommand,
    KeywordCommand keywordCommand,
    ConfigurationCommand configurationCommand,
    ClearCommand clearCommand,
    ServeCommand serveCommand,
    StatusCommand statusCommand)
{
    public RootCommand Create()
    {
        var root = CreateRootIndexCommand();
        root.Description = "CodeMeridian Indexer - unified CLI for indexing codebases into CodeMeridian.";

        root.Add(CreateIndexCommand(asRootCommand: false));
        root.Add(CreateInitCommand());
        root.Add(CreateKeywordsCommand());
        root.Add(CreateConfigurationCommand());
        root.Add(CreateServeCommand());
        root.Add(CreateDoctorCommand());
        root.Add(CreateVersionCommand());
        root.Add(CreateCheckDriftCommand());
        root.Add(CreateClearCommand());

        return root;
    }

    private RootCommand CreateRootIndexCommand() =>
        (RootCommand)CreateIndexCommand(asRootCommand: true);

    private Command CreateIndexCommand(bool asRootCommand)
    {
        Command command = asRootCommand
            ? new RootCommand()
            : new Command("index", "Index a codebase into CodeMeridian.");

        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory to scan. Defaults to the current directory." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name. If omitted, auto-detected from config or the target root." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var clearOption = new Option<bool>("--clear") { Description = "Remove existing knowledge before indexing. Applied only once." };
        var keywordsOption = new Option<bool>("--keywords") { Description = "Rebuild the derived keyword graph after indexing completes." };
        var skipDocsOption = new Option<bool>("--skip-docs") { Description = "Skip documentation ingestion." };
        skipDocsOption.Aliases.Add("--no-docs");
        var watchOption = new Option<bool>("--watch") { Description = "Watch mode. If both languages are present, C# watch runs first." };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Show what would be indexed without ingesting anything." };
        var listCapabilitiesOption = new Option<bool>("--list-capabilities") { Description = "Show available indexers on this machine." };
        var skipCSharpOption = new Option<bool>("--skip-csharp") { Description = "Skip C# indexing." };
        var skipTypeScriptOption = new Option<bool>("--skip-typescript") { Description = "Skip TypeScript/TSX indexing." };
        var skipConfigurationOption = new Option<bool>("--skip-config") { Description = "Skip configuration indexing." };
        var skipDiagnosticsOption = new Option<bool>("--skip-diagnostics") { Description = "Skip project-native compiler, TypeScript, and lint diagnostics indexing." };
        var allowRepoScriptsOption = new Option<bool>("--allow-repo-scripts") { Description = "Allow repo-controlled build and lint commands during diagnostics." };
        var noIncrementalOption = new Option<bool>("--no-incremental") { Description = "Ignore .meridian/cache and scan all enabled files." };
        noIncrementalOption.Aliases.Add("--force-full");
        var storageOption = new Option<string?>("--storage") { Description = "Cache storage mode for this run: repo or global." };

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(clearOption);
        command.Add(keywordsOption);
        command.Add(skipDocsOption);
        command.Add(watchOption);
        command.Add(dryRunOption);
        command.Add(listCapabilitiesOption);
        command.Add(skipCSharpOption);
        command.Add(skipTypeScriptOption);
        command.Add(skipConfigurationOption);
        command.Add(skipDiagnosticsOption);
        command.Add(allowRepoScriptsOption);
        command.Add(noIncrementalOption);
        command.Add(storageOption);

        command.SetAction(async parseResult =>
        {
            ResolvedIndexerSettings settings;
            try
            {
                IndexerStorageMode? storageMode = null;
                var storageValue = parseResult.GetValue(storageOption);
                if (!string.IsNullOrWhiteSpace(storageValue))
                {
                    storageMode = storageValue.Trim().ToLowerInvariant() switch
                    {
                        "repo" or "repository" => IndexerStorageMode.Repository,
                        "global" => IndexerStorageMode.Global,
                        _ => throw new InvalidOperationException("Invalid --storage value. Use repo or global."),
                    };
                }

                settings = settingsFactory.Create(new IndexCommandOptions(
                    parseResult.GetValue(pathArgument),
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(urlOption),
                    parseResult.GetValue(clearOption),
                    RebuildKeywords: parseResult.GetValue(keywordsOption),
                    IncludeDocs: !parseResult.GetValue(skipDocsOption),
                    Watch: parseResult.GetValue(watchOption),
                    DryRun: parseResult.GetValue(dryRunOption),
                    ListCapabilities: parseResult.GetValue(listCapabilitiesOption),
                    SkipCSharp: parseResult.GetValue(skipCSharpOption),
                    SkipTypeScript: parseResult.GetValue(skipTypeScriptOption),
                    SkipConfiguration: parseResult.GetValue(skipConfigurationOption),
                    SkipDiagnostics: parseResult.GetValue(skipDiagnosticsOption),
                    AllowRepoScripts: parseResult.GetValue(allowRepoScriptsOption),
                    Incremental: !parseResult.GetValue(noIncrementalOption),
                    Storage: storageMode));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }

            using var scope = services.CreateScope();
            var options = Options.Create(settings);
            var handler = ActivatorUtilities.CreateInstance<IndexCommandHandler>(scope.ServiceProvider, options);
            return await handler.RunAsync();
        });

        return command;
    }

    private Command CreateInitCommand()
    {
        var command = new Command("init", "Create or refresh CodeMeridian config for the current project.");
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory to initialize. Defaults to the current directory." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var forceOption = new Option<bool>("--force") { Description = "Overwrite generated config if it already exists." };
        var globalOption = new Option<bool>("--global") { Description = "Enable global cache/storage for this project." };

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(forceOption);
        command.Add(globalOption);

        command.SetAction(parseResult => initCommand.Run(new InitCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            parseResult.GetValue(forceOption),
            parseResult.GetValue(globalOption))));

        return command;
    }

    private Command CreateKeywordsCommand()
    {
        var command = new Command("keywords", "Manage the derived keyword graph without running a full index.");
        var rebuildCommand = new Command("rebuild", "Rebuild the derived keyword graph for one project or for all indexed projects.");
        rebuildCommand.Aliases.Add("index");
        var classifyCommand = new Command("classify", "Classify derived keywords without rebuilding the keyword graph relationships.");
        var statusCommand = new Command("status", "Check the status of a keyword rebuild or classification job.");

        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory used to resolve config defaults. Defaults to the current directory." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name. Omit to rebuild for all indexed projects." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var jobIdOption = new Option<Guid>("--job-id") { Description = "Keyword job id to check with the status command." };
        var rebuildBackgroundOption = new Option<bool>("--background") { Description = "Start the rebuild job in the background and return a job id immediately." };
        var rebuildWaitOption = new Option<bool>("--wait") { Description = "Wait until the rebuild job finishes before returning." };
        var rebuildTtlOption = new Option<int?>("--ttl-seconds") { Description = "Lease TTL for the background job lock in seconds. Defaults to 1800." };
        var classifyBackgroundOption = new Option<bool>("--background") { Description = "Start the classification job in the background and return a job id immediately." };
        var classifyWaitOption = new Option<bool>("--wait") { Description = "Wait until the classification job finishes before returning." };
        var classifyTtlOption = new Option<int?>("--ttl-seconds") { Description = "Lease TTL for the background job lock in seconds. Defaults to 1800." };

        rebuildCommand.Add(pathArgument);
        rebuildCommand.Add(projectOption);
        rebuildCommand.Add(urlOption);
        rebuildCommand.Add(rebuildBackgroundOption);
        rebuildCommand.Add(rebuildWaitOption);
        rebuildCommand.Add(rebuildTtlOption);

        rebuildCommand.SetAction(async parseResult => await keywordCommand.RunAsync(new KeywordCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            Background: parseResult.GetValue(rebuildBackgroundOption),
            Wait: parseResult.GetValue(rebuildWaitOption),
            LeaseTtlSeconds: parseResult.GetValue(rebuildTtlOption),
            Action: KeywordCommandAction.Rebuild)));

        classifyCommand.Add(pathArgument);
        classifyCommand.Add(projectOption);
        classifyCommand.Add(urlOption);
        classifyCommand.Add(classifyBackgroundOption);
        classifyCommand.Add(classifyWaitOption);
        classifyCommand.Add(classifyTtlOption);

        classifyCommand.SetAction(async parseResult => await keywordCommand.RunAsync(new KeywordCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            Background: parseResult.GetValue(classifyBackgroundOption),
            Wait: parseResult.GetValue(classifyWaitOption),
            LeaseTtlSeconds: parseResult.GetValue(classifyTtlOption),
            Action: KeywordCommandAction.Classify)));

        statusCommand.Add(pathArgument);
        statusCommand.Add(projectOption);
        statusCommand.Add(urlOption);
        statusCommand.Add(jobIdOption);

        statusCommand.SetAction(async parseResult => await keywordCommand.RunAsync(new KeywordCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            Background: false,
            Wait: false,
            LeaseTtlSeconds: null,
            JobId: parseResult.GetRequiredValue(jobIdOption),
            Action: KeywordCommandAction.Status)));

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(rebuildCommand);
        command.Add(classifyCommand);
        command.Add(statusCommand);

        command.SetAction(async parseResult => await keywordCommand.RunAsync(new KeywordCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            Action: KeywordCommandAction.Rebuild)));

        return command;
    }

    private Command CreateConfigurationCommand()
    {
        var command = new Command("config", "Manage the derived configuration graph without running a full code index.");
        var rebuildCommand = new Command("rebuild", "Rebuild configuration graph nodes and edges for one project.");

        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory used to resolve config defaults. Defaults to the current directory." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");

        rebuildCommand.Add(pathArgument);
        rebuildCommand.Add(projectOption);
        rebuildCommand.Add(urlOption);

        rebuildCommand.SetAction(async parseResult => await configurationCommand.RunAsync(new ConfigurationCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption))));

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(rebuildCommand);

        command.SetAction(async parseResult => await configurationCommand.RunAsync(new ConfigurationCommandOptions(
            parseResult.GetValue(pathArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption))));

        return command;
    }

    private Command CreateServeCommand()
    {
        var command = new Command("serve", "Create local MCP config and optionally start the backend stack.");
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory to initialize. Defaults to the current directory." };
        var hostOption = new Option<string>("--host") { DefaultValueFactory = _ => ServeOptions.DefaultHost, Description = "Hostname for generated MCP URLs." };
        var portOption = new Option<int>("--port") { DefaultValueFactory = _ => ServeOptions.DefaultPort, Description = "MCP server host port." };
        var neo4jHttpPortOption = new Option<int>("--neo4j-http-port") { DefaultValueFactory = _ => ServeOptions.DefaultNeo4jHttpPort, Description = "Neo4j browser host port." };
        var neo4jBoltPortOption = new Option<int>("--neo4j-bolt-port") { DefaultValueFactory = _ => ServeOptions.DefaultNeo4jBoltPort, Description = "Neo4j bolt host port." };
        var composeFileOption = new Option<string>("--compose-file") { DefaultValueFactory = _ => ServeOptions.DefaultComposeFileName, Description = "Compose file to create or use." };
        var imageOption = new Option<string>("--image") { DefaultValueFactory = _ => ServeOptions.DefaultImage, Description = "MCP server image." };
        var forceOption = new Option<bool>("--force") { Description = "Back up and overwrite generated files where needed." };
        var noStartOption = new Option<bool>("--no-start") { Description = "Write files but do not run docker compose." };

        command.Add(pathArgument);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(neo4jHttpPortOption);
        command.Add(neo4jBoltPortOption);
        command.Add(composeFileOption);
        command.Add(imageOption);
        command.Add(forceOption);
        command.Add(noStartOption);

        command.SetAction(async parseResult =>
        {
            var options = new ServeOptions(
                configurationService.ResolveRootPath(parseResult.GetValue(pathArgument)),
                parseResult.GetRequiredValue(hostOption),
                parseResult.GetValue(portOption),
                parseResult.GetValue(neo4jHttpPortOption),
                parseResult.GetValue(neo4jBoltPortOption),
                parseResult.GetRequiredValue(composeFileOption),
                parseResult.GetRequiredValue(imageOption),
                parseResult.GetValue(forceOption),
                Start: !parseResult.GetValue(noStartOption));

            return await serveCommand.RunAsync(options);
        });

        return command;
    }

    private Command CreateDoctorCommand()
    {
        var command = new Command("doctor", "Check CodeMeridian backend and graph health.");
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory used to resolve config defaults." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);

        command.SetAction(async parseResult =>
        {
            var context = configurationService.CreateContext(parseResult.GetValue(pathArgument));
            var project = configurationService.ResolveProject(context, parseResult.GetValue(projectOption));
            var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, parseResult.GetValue(urlOption));
            return await statusCommand.RunDoctorAsync(project, codeMeridianUrl, context.ApiKey);
        });

        return command;
    }

    private Command CreateVersionCommand()
    {
        var command = new Command("version", "Show the local tool version and try to fetch the MCP server version.");
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory used to resolve config defaults." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");

        command.Add(pathArgument);
        command.Add(urlOption);

        command.SetAction(async parseResult =>
        {
            var context = configurationService.CreateContext(parseResult.GetValue(pathArgument));
            var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, parseResult.GetValue(urlOption));
            return await statusCommand.RunVersionAsync(codeMeridianUrl, context.ApiKey);
        });

        return command;
    }

    private Command CreateCheckDriftCommand()
    {
        var command = new Command("check-drift", "Verify graph drift and freshness.");
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory used to resolve config defaults." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var failOnOption = new Option<string>("--fail-on") { DefaultValueFactory = _ => "high", Description = "Drift threshold: low, moderate, or high." };

        command.Add(pathArgument);
        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(failOnOption);

        command.SetAction(async parseResult =>
        {
            var context = configurationService.CreateContext(parseResult.GetValue(pathArgument));
            var project = configurationService.ResolveProject(context, parseResult.GetValue(projectOption));
            var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, parseResult.GetValue(urlOption));
            return await statusCommand.RunDriftVerificationAsync(project, codeMeridianUrl, context.ApiKey, parseResult.GetRequiredValue(failOnOption));
        });

        return command;
    }

    private Command CreateClearCommand()
    {
        var command = new Command("clear", "Remove indexed knowledge from CodeMeridian.");
        var projectOption = new Option<string?>("--project") { Description = "Remove code graph nodes and documents for one project." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var allCodeGraphOption = new Option<bool>("--all-code-graph") { Description = "Remove all CodeNode graph data for every project. Documents are preserved." };

        command.Add(projectOption);
        command.Add(urlOption);
        command.Add(allCodeGraphOption);

        command.SetAction(async parseResult => await clearCommand.RunAsync(new ClearCommandOptions(
            parseResult.GetValue(projectOption),
            parseResult.GetValue(urlOption),
            parseResult.GetValue(allCodeGraphOption))));

        return command;
    }
}
