using CodeMeridian.Application.Extensions;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services.AddApplication(null);
    }

    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration)
    {
        if (configuration is not null)
        {
            services.Configure<CodebaseAnalysisOptions>(configuration.GetSection("analysis"));
            services.Configure<CodebaseAnalysisOptions>(configuration.GetSection("CodeMeridian:Analysis"));
            services.Configure<CodebaseIndexingOptions>(configuration.GetSection("indexing"));
            services.Configure<CodebaseIndexingOptions>(configuration.GetSection("CodeMeridian:Indexing"));
            services.Configure<KeywordEnrichmentOptions>(configuration.GetSection(KeywordEnrichmentOptions.SectionName));
            services.Configure<KeywordEnrichmentOptions>(configuration.GetSection($"CodeMeridian:{KeywordEnrichmentOptions.SectionName}"));
            services.Configure<KeywordClassificationOptions>(configuration.GetSection(KeywordClassificationOptions.SectionName));
            services.Configure<KeywordClassificationOptions>(configuration.GetSection($"CodeMeridian:{KeywordClassificationOptions.SectionName}"));
        }
        else
        {
            services.AddOptions<CodebaseAnalysisOptions>();
            services.AddOptions<CodebaseIndexingOptions>();
            services.AddOptions<KeywordEnrichmentOptions>();
            services.AddOptions<KeywordClassificationOptions>();
        }

        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<IIndexedFileRoleClassifier, ConfiguredIndexedFileRoleClassifier>();
        services.AddSingleton<IAnalysisProfilePolicy, DefaultAnalysisProfilePolicy>();
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<ICodebaseQueryService, CodebaseQueryService>();
        services.AddTransient<ICodebaseStatusService, CodebaseStatusService>();
        services.AddSingleton<IKeywordExtractionService, DefaultKeywordExtractionService>();
        services.AddTransient<IKeywordGraphService, KeywordGraphService>();
        services.AddSingleton<IKeywordGraphJobService, KeywordGraphJobService>();

        return services;
    }
}
