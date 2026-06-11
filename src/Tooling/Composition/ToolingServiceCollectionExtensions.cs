using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Tooling.Composition;

public static class ToolingServiceCollectionExtensions
{
    public static IServiceCollection AddCodeMeridianTooling(this IServiceCollection services)
    {
        services.AddOptions<ToolCliDefaults>();
        services.AddSingleton<IConfigureOptions<ToolCliDefaults>, ConfigureToolCliDefaults>();
        services.AddSingleton<CodeMeridianConfigFileStore>();
        services.AddSingleton<IProjectDiscoveryService, ProjectDiscoveryService>();
        services.AddSingleton<IToolConfigurationService, ToolConfigurationService>();
        return services;
    }
}
