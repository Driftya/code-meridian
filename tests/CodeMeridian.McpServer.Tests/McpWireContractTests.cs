using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpWireContractTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpWireContractTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(null, "mcp-modern-baseline.json")]
    [InlineData("2025-11-25", "mcp-downlevel-baseline.json")]
    public async Task RawWireExchange_MatchesBoundedProtocolBaseline(
        string? pinnedProtocolVersion,
        string snapshotName)
    {
        var recorder = new McpWireCaptureHandler();
        using var httpClient = _factory.CreateDefaultClient(recorder);
        await using var client = await McpTestClient.CreateAsync(httpClient, pinnedProtocolVersion);
        var listResult = await client.ListToolsAsync(new ListToolsRequestParams());
        var exchanges = recorder.Exchanges;
        var handshake = exchanges.First(exchange =>
            exchange.RequestBody.Contains(
                pinnedProtocolVersion is null ? "\"server/discover\"" : "\"initialize\"",
                StringComparison.Ordinal));
        var toolsList = exchanges.First(exchange =>
            exchange.RequestBody.Contains("\"tools/list\"", StringComparison.Ordinal));

        handshake.ResponseBody.Should().Contain("capabilities");
        toolsList.ResponseBody.Should().Contain("inputSchema");
        toolsList.ResponseBody.Should().Contain("annotations");
        Encoding.UTF8.GetByteCount(toolsList.ResponseBody).Should().BeLessThan(512 * 1024);
        toolsList.ResponseBody.Should().NotContain(GraphQlWebApplicationFactory.ApiKey);
        exchanges.Should().OnlyContain(exchange =>
            !exchange.RequestHeaders.ContainsKey("Authorization"));
        exchanges.Should().OnlyContain(exchange =>
            !exchange.ResponseHeaders.Keys.Any(key =>
                key.Equals("Mcp-Session-Id", StringComparison.OrdinalIgnoreCase)));

        var baseline = JsonSerializer.SerializeToNode(new
        {
            protocolVersion = client.NegotiatedProtocolVersion,
            handshakeMethod = pinnedProtocolVersion is null ? "server/discover" : "initialize",
            toolCount = listResult.Tools.Count,
            annotationCount = listResult.Tools.Count(tool => tool.Annotations is not null),
            structuredToolNames = listResult.Tools
                .Where(tool => tool.OutputSchema is not null)
                .Select(tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            tasksAdvertised = client.ServerCapabilities.Extensions?
                .ContainsKey("io.modelcontextprotocol/tasks") ?? false,
            appsAdvertised = client.ServerCapabilities.Extensions?
                .ContainsKey("io.modelcontextprotocol/ui") ?? false,
            toolCatalogTimeToLiveSeconds = listResult.TimeToLive?.TotalSeconds,
            toolCatalogCacheScope = listResult.CacheScope?.ToString().ToLowerInvariant()
        });
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName);
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath));

        JsonNode.DeepEquals(baseline, expected).Should().BeTrue(
            $"bounded wire baseline {snapshotName} must be updated intentionally; " +
            $"actual: {baseline?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
    }
}
