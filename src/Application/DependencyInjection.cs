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
            services.Configure<KeywordEnrichmentOptions>(configuration.GetSection(KeywordEnrichmentOptions.SectionName));
            services.Configure<KeywordEnrichmentOptions>(configuration.GetSection($"CodeMeridian:{KeywordEnrichmentOptions.SectionName}"));
        }
        else
        {
            services.AddOptions<CodebaseAnalysisOptions>();
            services.AddOptions<KeywordEnrichmentOptions>();
        }

        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddTransient<ICodebaseQueryService, CodebaseQueryService>();
        services.AddTransient<ICodebaseStatusService, CodebaseStatusService>();
        services.AddSingleton<IKeywordExtractionService, DefaultKeywordExtractionService>();
        services.AddTransient<IKeywordGraphService, KeywordGraphService>();

        return services;
    }
}
