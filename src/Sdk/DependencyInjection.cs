using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace CodeMeridian.Sdk;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="CodeMeridianClient"/> as a typed HTTP client.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="CodeMeridianBaseUrl">Base URL of the running CodeMeridian API (e.g. "http://localhost:5000").</param>
    /// <param name="apiKey">Optional CodeMeridian API key sent as a bearer token.</param>
    public static IServiceCollection AddCodeMeridianClient(
        this IServiceCollection services,
        string CodeMeridianBaseUrl,
        string? apiKey = null)
    {
        services.AddHttpClient<CodeMeridianClient>(client =>
        {
            client.BaseAddress = new Uri(CodeMeridianBaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}
