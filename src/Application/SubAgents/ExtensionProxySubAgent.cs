using System.Net.Http.Json;
using CodeMeridian.Core.Agents;
using CodeMeridian.Core.Extensions;

namespace CodeMeridian.Application.SubAgents;

/// <summary>
/// Proxies requests to a registered external extension agent via HTTP.
/// The extension must expose a POST endpoint that accepts AgentRequest
/// and returns AgentResponse (same contract as CodeMeridian's own API).
/// </summary>
public sealed class ExtensionProxySubAgent(
    AgentExtension extension,
    IHttpClientFactory httpClientFactory) : ISubAgent
{
    public string Name => extension.Name;
    public string Description => extension.Description;
    public IReadOnlyList<string> Capabilities => extension.Capabilities;

    public async Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CodeMeridianExtension");

        try
        {
            var response = await client.PostAsJsonAsync(extension.Endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<AgentResponse>(cancellationToken: cancellationToken);

            return result ?? new AgentResponse
            {
                Content = "Extension returned an empty response.",
                AgentName = Name,
                IsSuccess = false
            };
        }
        catch (Exception ex)
        {
            extension.IsHealthy = false;
            return new AgentResponse
            {
                Content = $"Extension '{Name}' is unavailable.",
                AgentName = Name,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
