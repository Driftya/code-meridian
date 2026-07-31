using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Projects;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Application.Sensors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeMeridian.Evolution.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEvolutionApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CognitiveLedgerService>();
        services.AddSingleton<SensorRegistry>();
        services.AddSingleton<SensorRunner>();
        services.AddSingleton<ReasoningRuntime>();
        services.AddSingleton<CognitiveMind>();
        services.AddSingleton<ProjectRegistry>();
        return services;
    }
}
