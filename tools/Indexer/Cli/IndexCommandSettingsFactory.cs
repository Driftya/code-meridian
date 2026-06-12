using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Configuration;

internal sealed class IndexCommandSettingsFactory(IToolConfigurationService configurationService)
{
    public ResolvedIndexerSettings Create(IndexCommandOptions options)
    {
        var context = configurationService.CreateContext(options.Path);
        var project = configurationService.ResolveProject(context, options.Project);

        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("Project name could not be resolved. Use --project <name> or check meridian.json.");

        return new ResolvedIndexerSettings
        {
            RootPath = context.RootPath,
            Project = project,
            CodeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl),
            ApiKey = context.ApiKey,
            Clear = options.Clear,
            RebuildKeywords = options.RebuildKeywords,
            IncludeDocs = options.IncludeDocs,
            Watch = options.Watch,
            DryRun = options.DryRun,
            ListCapabilities = options.ListCapabilities,
            SkipCSharp = options.SkipCSharp,
            SkipTypeScript = options.SkipTypeScript,
            SkipDiagnostics = options.SkipDiagnostics,
            AllowRepoScripts = configurationService.ResolveAllowRepoScripts(context, options.AllowRepoScripts),
            Incremental = options.Incremental,
            StorageMode = options.Storage
                ?? ((context.LocalConfig?.UseGlobalCache ?? context.GlobalConfig?.UseGlobalCache) == true
                    ? IndexerStorageMode.Global
                    : IndexerStorageMode.Repository)
        };
    }
}
