using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Sdk;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="CodeMeridianClient"/> as a typed HTTP client.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="CodeMeridianBaseUrl">Base URL of the running CodeMeridian API (e.g. "http://localhost:5000").</param>
    public static IServiceCollection AddCodeMeridianClient(
        this IServiceCollection services,
        string CodeMeridianBaseUrl)
    {
        services.AddHttpClient<CodeMeridianClient>(client =>
        {
            client.BaseAddress = new Uri(CodeMeridianBaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        return services;
    }
}
