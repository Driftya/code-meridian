using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Application.Sensors;
using CodeMeridian.Evolution.Infrastructure.Journal;
using CodeMeridian.Evolution.Infrastructure.Reasoning;
using CodeMeridian.Evolution.Infrastructure.Sensors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CodeMeridian.Evolution.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEvolutionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration["ConnectionStrings:Evolution"];
        var useInMemory = bool.TryParse(
            configuration["Evolution:Storage:UseInMemory"],
            out var configuredInMemory) && configuredInMemory;

        if (useInMemory || string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IJournalStore, InMemoryJournalStore>();
        }
        else
        {
            services.AddSingleton(NpgsqlDataSource.Create(connectionString));
            services.AddSingleton<IJournalStore, PostgreSqlJournalStore>();
        }

        services.AddSingleton<ISensor, LifecycleSensor>();
        services.AddSingleton<ISensor, SystemResourceSensor>();
        services.AddSingleton<ISensor, LedgerIntegritySensor>();
        services.Configure<InternetFeedOptions>(
            configuration.GetSection("Evolution:Sensors:InternetFeed"));
        services.Configure<CodeMeridianSensorOptions>(
            configuration.GetSection("Evolution:Sensors:CodeMeridian"));
        services.Configure<ChatModelOptions>(
            configuration.GetSection("Evolution:Reasoning:ChatModel"));
        services.AddHttpClient("evolution-internet-feed", client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient("evolution-codemeridian", client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient("evolution-chat-model", client =>
            client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<HumanPromptSensor>();
        services.AddSingleton<IPromptReceiver>(provider =>
            provider.GetRequiredService<HumanPromptSensor>());
        services.AddSingleton<ISensor>(provider =>
            provider.GetRequiredService<HumanPromptSensor>());
        services.AddSingleton<ISensor, InternetFeedSensor>();
        services.AddSingleton<ISensor, CodeMeridianGraphSensor>();
        services.AddSingleton<IReasoningProvider, FakeReasoningProvider>();
        services.AddSingleton<IReasoningProvider, ChatCompletionsReasoningProvider>();
        return services;
    }
}
