using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ConfigurationCommand(IToolConfigurationService configurationService)
{
    public async Task<int> RunAsync(ConfigurationCommandOptions options)
    {
        var context = configurationService.CreateContext(options.Path);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);
        var project = configurationService.ResolveProject(context, options.Project, includeFallback: false);

        if (string.IsNullOrWhiteSpace(project))
        {
            Console.Error.WriteLine("error: project name is required for configuration indexing.");
            return 1;
        }

        try
        {
            Console.WriteLine($"Rebuilding configuration graph at {codeMeridianUrl} for '{project}'...");
            var indexer = new ConfigurationIndexer();
            await indexer.RunAsync(
                context.RootPath,
                project,
                codeMeridianUrl,
                context.ApiKey,
                IndexedFileRoleClassifierFactory.Create(context.LocalConfig?.FileRoles ?? context.GlobalConfig?.FileRoles),
                context.LocalConfig?.ConfigurationFiles ?? context.GlobalConfig?.ConfigurationFiles,
                context.LocalConfig?.ArchitecturePath ?? context.GlobalConfig?.ArchitecturePath ?? CodeMeridianConfigFileStore.DefaultArchitecturePath,
                clearExistingConfiguration: true);
            Console.WriteLine($"Configuration graph rebuilt for '{project}'.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: configuration graph rebuild failed: {ex.Message}");
            return 1;
        }
    }
}
