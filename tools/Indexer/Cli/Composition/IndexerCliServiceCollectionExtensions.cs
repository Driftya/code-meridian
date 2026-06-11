using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Indexer.Cli.Composition;

internal static class IndexerCliServiceCollectionExtensions
{
    public static IServiceCollection AddIndexerCli(this IServiceCollection services)
    {
        services.AddCodeMeridianTooling();
        services.AddTransient<IndexCommandSettingsFactory>();
        services.AddTransient<RootCommandFactory>();
        services.AddTransient<IndexCommandHandler>();
        services.AddTransient<DiagnosticsCommand>();
        services.AddTransient<InitCommand>();
        services.AddTransient<ClearCommand>();
        services.AddTransient<ServeCommand>();
        services.AddTransient<StatusCommand>();
        services.AddTransient<ServeWriter>();

        return services;
    }
}
