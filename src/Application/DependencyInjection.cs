using CodeMeridian.Application.Extensions;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Extension registry — singleton so registrations survive across requests
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();

        // Query service — provides structured facts to MCP tools (Copilot does the reasoning)
        services.AddTransient<ICodebaseQueryService, CodebaseQueryService>();
        services.AddTransient<ICodebaseStatusService, CodebaseStatusService>();

        return services;
    }
}
