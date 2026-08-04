using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpProtocolCompatibilityTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private const string ModernProtocolVersion = "2026-07-28";
    private const string DownLevelProtocolVersion = "2025-11-25";
    private readonly GraphQlWebApplicationFactory _factory;

    public McpProtocolCompatibilityTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DefaultClient_NegotiatesModernStatelessProtocol()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        client.NegotiatedProtocolVersion.Should().Be(ModernProtocolVersion);
        (await client.ListToolsAsync()).Should().HaveCount(62);
    }

    [Fact]
    public async Task PinnedDownLevelClient_UsesInitializeHandshakeAndRetainsToolContracts()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient, DownLevelProtocolVersion);
        var tools = await client.ListToolsAsync();

        client.NegotiatedProtocolVersion.Should().Be(DownLevelProtocolVersion);
        tools.Should().HaveCount(62);
        tools.Should().OnlyContain(tool => tool.ProtocolTool.InputSchema.ValueKind == JsonValueKind.Object);
        tools.Should().OnlyContain(tool => tool.ProtocolTool.Annotations != null);
        tools.Single(tool => tool.Name == "find_connection").ProtocolTool.OutputSchema.Should().NotBeNull();
    }

    [Fact]
    public async Task DownLevelClient_CallsStructuredToolWithUsefulTextFallback()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindConnectionResultAsync("source", "target", Arg.Any<CancellationToken>())
            .Returns(new ConnectionAnalysisResult(
                "1.0", "source", "target", 10, false, 0, false, [], [], []));
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodebaseQueryService>();
                services.AddSingleton(queryService);
            }));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient, DownLevelProtocolVersion);

        var result = await client.CallToolAsync(
            "find_connection",
            new Dictionary<string, object?> { ["fromId"] = "source", ["toId"] = "target" });

        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("No path found");
        result.StructuredContent.Should().NotBeNull();
    }

    [Fact]
    public async Task DownLevelClient_CallsTaskCapableToolAsOrdinaryRequest()
    {
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("Keyword graph rebuilt.");
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKeywordGraphService>();
                services.AddSingleton(keywordGraph);
            }));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient, DownLevelProtocolVersion);

        var result = await client.CallToolAsync(
            "rebuild_keyword_graph",
            new Dictionary<string, object?> { ["projectContext"] = "CodeMeridian" });

        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("Keyword graph rebuilt");
    }

    [Fact]
    public void ProtocolToolDeserializer_RejectsMissingInputSchema()
    {
        var deserialize = () => JsonSerializer.Deserialize<Tool>(
            """{"name":"invalid-contract"}""",
            ModelContextProtocol.McpJsonUtilities.DefaultOptions);

        deserialize.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("server/discover")]
    [InlineData("tools/list")]
    [InlineData("tools/call")]
    [InlineData("tasks/get")]
    [InlineData("tasks/update")]
    [InlineData("tasks/cancel")]
    public async Task McpMethod_WithoutAuthentication_ReturnsUnauthorized(string method)
    {
        using var httpClient = _factory.CreateClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sse")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method,
                    @params = new { }
                }),
                Encoding.UTF8,
                "application/json")
        };

        var response = await httpClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl?.Public.Should().NotBe(true);
    }

    [Fact]
    public async Task StatelessEndpoint_DoesNotRequireLegacyGetEventStream()
    {
        using var httpClient = _factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GraphQlWebApplicationFactory.ApiKey);

        var response = await httpClient.GetAsync("/sse");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.MethodNotAllowed);
        response.Headers.Should().NotContain(header =>
            header.Key.Equals("Mcp-Session-Id", StringComparison.OrdinalIgnoreCase));
    }
}
