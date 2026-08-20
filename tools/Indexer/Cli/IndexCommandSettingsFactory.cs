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

        if (options.ExternalOnly && options.Clear)
            throw new InvalidOperationException("--external-only cannot be used with --clear because clearing would remove the primary root's indexed knowledge.");

        if (options.ExternalOnly && options.Watch)
            throw new InvalidOperationException("--external-only cannot be used with --watch because external project directories are not watched.");

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
            ExternalOnly = options.ExternalOnly,
            SkipTypeScript = options.SkipTypeScript,
            SkipConfiguration = options.SkipConfiguration,
            ConfigurationFiles = context.LocalConfig?.ConfigurationFiles ?? context.GlobalConfig?.ConfigurationFiles,
            ArchitecturePath = context.LocalConfig?.ArchitecturePath ?? context.GlobalConfig?.ArchitecturePath ?? CodeMeridianConfigFileStore.DefaultArchitecturePath,
            FileRoles = context.LocalConfig?.FileRoles ?? context.GlobalConfig?.FileRoles,
            EmbeddingEnabled = context.LocalConfig?.EmbeddingEnabled ?? context.GlobalConfig?.EmbeddingEnabled,
            SkipDiagnostics = options.SkipDiagnostics,
            AllowRepoScripts = configurationService.ResolveAllowRepoScripts(context, options.AllowRepoScripts),
            Incremental = options.Incremental,
            StorageMode = options.Storage
                ?? ((context.LocalConfig?.UseGlobalCache ?? context.GlobalConfig?.UseGlobalCache) == true
                    ? IndexerStorageMode.Global
                    : IndexerStorageMode.Repository),
            HasOutdatedLocalConfig = context.LocalConfig is not null
                                     && context.LocalConfig.Version < CodeMeridianConfigFileStore.CurrentConfigVersion,
            LocalConfigVersion = context.LocalConfig?.Version ?? 0,
            CurrentConfigVersion = CodeMeridianConfigFileStore.CurrentConfigVersion
        };
    }
}
