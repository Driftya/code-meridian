using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using CodeMeridian.Infrastructure.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<Neo4jOptions>(configuration.GetSection(Neo4jOptions.SectionName));
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));

        // Register concrete types so the initializer can access them directly
        services.AddSingleton<Neo4jCodeGraphRepository>();
        services.AddSingleton<Neo4jVectorRepository>();

        // Expose via domain interfaces
        services.AddSingleton<ICodeGraphRepository>(sp =>
            sp.GetRequiredService<Neo4jCodeGraphRepository>());
        services.AddSingleton<IVectorRepository>(sp =>
            sp.GetRequiredService<Neo4jVectorRepository>());

        // Register embedding provider based on configuration
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new();
            
            if (!options.Enabled)
                return new NoOpEmbeddingProvider();

            return options.Provider switch
            {
                "OpenAI" => new OpenAiEmbeddingProvider(
                    new HttpClient(),
                    Microsoft.Extensions.Options.Options.Create(options),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiEmbeddingProvider>>()),
                "Ollama" => new OllamaEmbeddingProvider(
                    new HttpClient(),
                    Microsoft.Extensions.Options.Options.Create(options),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaEmbeddingProvider>>()),
                "Stub" => new StubEmbeddingProvider(),
                _ => new NoOpEmbeddingProvider()
            };
        });

        // Initialize Neo4j schema on startup
        services.AddHostedService<Neo4jInitializationService>();

        return services;
    }
}
