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

        // Register concrete types so the initializer can access them directly
        services.AddSingleton<Neo4jCodeGraphRepository>();
        services.AddSingleton<Neo4jVectorRepository>();

        // Expose via domain interfaces
        services.AddSingleton<ICodeGraphRepository>(sp =>
            sp.GetRequiredService<Neo4jCodeGraphRepository>());
        services.AddSingleton<IVectorRepository>(sp =>
            sp.GetRequiredService<Neo4jVectorRepository>());

        // Initialize Neo4j schema on startup
        services.AddHostedService<Neo4jInitializationService>();

        return services;
    }
}
