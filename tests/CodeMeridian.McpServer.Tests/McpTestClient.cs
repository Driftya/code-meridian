using System.Net.Http.Headers;
using ModelContextProtocol.Client;

namespace CodeMeridian.McpServer.Tests;

internal static class McpTestClient
{
    public static async Task<McpClient> CreateAsync(
        HttpClient httpClient,
        string? protocolVersion = null)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GraphQlWebApplicationFactory.ApiKey);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Name = "CodeMeridian integration test",
                Endpoint = new Uri(httpClient.BaseAddress!, "/sse"),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false);

        var options = protocolVersion is null
            ? null
            : new McpClientOptions { ProtocolVersion = protocolVersion };

        return await McpClient.CreateAsync(transport, options);
    }
}
