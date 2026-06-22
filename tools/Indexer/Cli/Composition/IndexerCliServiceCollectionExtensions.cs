using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Indexer.Cli.SessionEvaluation;
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
        services.AddTransient<KeywordCommand>();
        services.AddTransient<ConfigurationCommand>();
        services.AddTransient<InitCommand>();
        services.AddTransient<ClearCommand>();
        services.AddTransient<ServeCommand>();
        services.AddTransient<StatusCommand>();
        services.AddTransient<PrContextReportCommand>();
        services.AddTransient<SessionEvaluationCommand>();
        services.AddTransient<SessionUsefulnessEvaluator>();
        services.AddTransient<SessionEvidenceReader>();
        services.AddTransient<ISessionChangeSource, GitSessionChangeSource>();
        services.AddTransient<IPrContextGitDiffProvider, PrContextGitDiffProvider>();
        services.AddTransient<ServeWriter>();
        services.AddTransient<IInitPromptService, ConsoleInitPromptService>();

        return services;
    }
}
