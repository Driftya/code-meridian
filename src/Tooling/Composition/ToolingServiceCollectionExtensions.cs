using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
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
        services.AddSingleton<IIndexerStoragePathService, IndexerStoragePathService>();
        services.AddSingleton<IToolConfigurationService, ToolConfigurationService>();
        return services;
    }
}
