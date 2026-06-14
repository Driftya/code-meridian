using System.CommandLine;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Composition;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddCodeMeridianTooling();
services.AddSingleton<RoslynRootCommandFactory>();
await using var provider = services.BuildServiceProvider();

var rootCommand = provider.GetRequiredService<RoslynRootCommandFactory>().Create();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();

internal sealed class RoslynRootCommandFactory(
    IServiceProvider services,
    IToolConfigurationService configurationService)
{
    public RootCommand Create()
    {
        var root = new RootCommand("CodeMeridian Roslyn indexer for C# codebases.");
        var pathArgument = new Argument<string>("path") { Description = "Root directory of the project to index" };
        var projectOption = new Option<string?>("--project") { Description = "Project context name. If omitted, auto-detected from the target root." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian base URL." };
        urlOption.Aliases.Add("--CodeMeridian");
        var clearOption = new Option<bool>("--clear") { Description = "Remove existing knowledge before indexing." };
        var watchOption = new Option<bool>("--watch") { Description = "Stay running and re-index when .cs files change." };

        root.Add(pathArgument);
        root.Add(projectOption);
        root.Add(urlOption);
        root.Add(clearOption);
        root.Add(watchOption);

        root.SetAction(async parseResult =>
        {
            var context = configurationService.CreateContext(parseResult.GetRequiredValue(pathArgument));
            var settings = new RoslynIndexerSettings
            {
                RootPath = context.RootPath,
                Project = configurationService.ResolveProject(context, parseResult.GetValue(projectOption)),
                CodeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, parseResult.GetValue(urlOption)),
                ApiKey = context.ApiKey,
                FileRoles = context.LocalConfig?.FileRoles ?? context.GlobalConfig?.FileRoles,
                Clear = parseResult.GetValue(clearOption),
                Watch = parseResult.GetValue(watchOption)
            };

            if (!settings.RootPath.Exists)
            {
                Console.Error.WriteLine($"error: directory not found: {settings.RootPath.FullName}");
                return 1;
            }

            using var scope = services.CreateScope();
            var options = Options.Create(settings);
            var handler = ActivatorUtilities.CreateInstance<RoslynIndexCommandHandler>(scope.ServiceProvider, options);
            return await handler.RunAsync();
        });

        return root;
    }
}

internal sealed class RoslynIndexerSettings
{
    public required DirectoryInfo RootPath { get; init; }
    public required string Project { get; init; }
    public required string CodeMeridianUrl { get; init; }
    public string? ApiKey { get; init; }
    public CodeMeridianFileRolePatternSnapshot? FileRoles { get; init; }
    public bool Clear { get; init; }
    public bool Watch { get; init; }
}

internal sealed class RoslynIndexCommandHandler(IOptions<RoslynIndexerSettings> settings)
{
    private readonly RoslynIndexerSettings _settings = settings.Value;

    public async Task<int> RunAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        services.AddCodeMeridianClient(_settings.CodeMeridianUrl, _settings.ApiKey);
        services.AddSingleton<IIndexedFileRoleClassifier>(IndexedFileRoleClassifierFactory.Create(_settings.FileRoles));
        services.AddTransient<CSharpIndexer>();
        services.AddTransient<IndexerPipeline>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IndexerPipeline>();

        await pipeline.RunAsync(_settings.RootPath, _settings.Project, _settings.Clear);

        if (!_settings.Watch)
            return 0;

        var logger = provider.GetRequiredService<ILogger<IndexerPipeline>>();
        logger.LogInformation("Watch mode active monitoring {Path} for .cs and .md changes. Press Ctrl+C to exit.", _settings.RootPath.FullName);

        System.Timers.Timer? debounceTimer = null;
        var changedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedDuringDebounce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        void ScheduleReindex(string fullPath, bool deleted)
        {
            var relativePath = Path.GetRelativePath(_settings.RootPath.FullName, fullPath).Replace('\\', '/');
            if (deleted)
                deletedDuringDebounce.Add(relativePath);
            else
                changedDuringDebounce.Add(relativePath);

            debounceTimer?.Stop();
            debounceTimer?.Dispose();
            debounceTimer = new System.Timers.Timer(2_000) { AutoReset = false };
            debounceTimer.Elapsed += async (_, _) =>
            {
                logger.LogInformation("[watch] Change detected re-indexing...");
                var changedBatch = changedDuringDebounce.ToArray();
                var deletedBatch = deletedDuringDebounce.ToArray();
                changedDuringDebounce.Clear();
                deletedDuringDebounce.Clear();

                try
                {
                    await pipeline.RunAsync(
                        _settings.RootPath,
                        _settings.Project,
                        clear: false,
                        changedFiles: changedBatch,
                        deletedFiles: deletedBatch,
                        cancellationToken: cts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[watch] Re-index failed.");
                }
            };
            debounceTimer.Start();
        }

        using var fsWatcher = new FileSystemWatcher(_settings.RootPath.FullName)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        fsWatcher.Filters.Add("*.cs");
        fsWatcher.Changed += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
        fsWatcher.Created += (_, e) => ScheduleReindex(e.FullPath, deleted: false);
        fsWatcher.Deleted += (_, e) => ScheduleReindex(e.FullPath, deleted: true);
        fsWatcher.Renamed += (_, e) =>
        {
            ScheduleReindex(e.OldFullPath, deleted: true);
            ScheduleReindex(e.FullPath, deleted: false);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("Watch mode stopped.");
        return 0;
    }
}
