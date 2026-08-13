using System.Text.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using ModelContextProtocol.Protocol;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpAppsEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private static readonly string[] DestructiveToolNames =
    [
        "clear_project_knowledge",
        "clear_code_graph"
    ];

    private readonly GraphQlWebApplicationFactory _factory;

    public McpAppsEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StreamableHttpEndpoint_LeavesExperimentalAppsDisabledByDefault()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var tools = await client.ListToolsAsync();
        var contractTool = tools.Single(tool => tool.Name == "get_client_extension_contract");
        var connectionTool = tools.Single(tool => tool.Name == "find_connection");
        var challengeTool = tools.Single(tool => tool.Name == "start_change_context_challenge");

        client.ServerCapabilities.Extensions.Should().NotContainKey("io.modelcontextprotocol/ui");
        contractTool.ProtocolTool.Meta.Should().BeNullOrEmpty();
        connectionTool.ProtocolTool.Meta.Should().BeNullOrEmpty();
        challengeTool.ProtocolTool.Meta.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task StreamableHttpEndpoint_AdvertisesAndServesFeatureFlaggedApps()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Apps:Enabled", "true"));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        client.ServerCapabilities.Extensions.Should().ContainKey("io.modelcontextprotocol/ui");

        var tools = await client.ListToolsAsync();
        AssertAppMetadata(
            tools.Single(tool => tool.Name == "get_client_extension_contract").ProtocolTool.Meta,
            "ui://code-meridian/client-extension-contract");
        AssertAppMetadata(
            tools.Single(tool => tool.Name == "find_connection").ProtocolTool.Meta,
            "ui://code-meridian/connection-viewer");
        AssertAppMetadata(
            tools.Single(tool => tool.Name == "start_change_context_challenge").ProtocolTool.Meta,
            "ui://code-meridian/change-context-challenge",
            "model", "app");
        AssertAppMetadata(
            tools.Single(tool => tool.Name == "answer_change_context_challenge").ProtocolTool.Meta,
            "ui://code-meridian/change-context-challenge",
            "app");
        AssertAppMetadata(
            tools.Single(tool => tool.Name == "record_change_context_challenge_note").ProtocolTool.Meta,
            "ui://code-meridian/change-context-challenge",
            "app");

        var resources = await client.ListResourcesAsync();
        var contractResource = resources.Single(item =>
            item.Uri == "ui://code-meridian/client-extension-contract");
        contractResource.MimeType.Should().Be("text/html;profile=mcp-app");
        AssertEmptyCsp(contractResource.ProtocolResource.Meta);
        var contractHtml = (await contractResource.ReadAsync())
            .Contents.OfType<TextResourceContents>()
            .Should().ContainSingle().Subject.Text;
        contractHtml.Should().Contain("Client Extension Contract");
        contractHtml.Should().Contain("get_client_extension_contract");
        contractHtml.Should().NotContain("CodeMeridian_Auth_ApiKey");
        contractHtml.Should().NotContain("X-CodeMeridian-ApiKey");
        contractHtml.Should().NotContain("Authorization");
        Encoding.UTF8.GetByteCount(contractHtml).Should().BeLessThan(64 * 1024);
        foreach (var destructiveTool in DestructiveToolNames)
            contractHtml.Should().NotContain(destructiveTool);

        var connectionResource = resources.Single(item =>
            item.Uri == "ui://code-meridian/connection-viewer");
        connectionResource.MimeType.Should().Be("text/html;profile=mcp-app");
        AssertEmptyCsp(connectionResource.ProtocolResource.Meta);
        var connectionHtml = (await connectionResource.ReadAsync())
            .Contents.OfType<TextResourceContents>()
            .Should().ContainSingle().Subject.Text;
        connectionHtml.Should().Contain("Connection Viewer");
        connectionHtml.Should().Contain("find_connection");
        connectionHtml.Should().NotContain("innerHTML");
        connectionHtml.Should().NotContain("Authorization");
        Encoding.UTF8.GetByteCount(connectionHtml).Should().BeLessThan(64 * 1024);

        var challengeResource = resources.Single(item =>
            item.Uri == "ui://code-meridian/change-context-challenge");
        challengeResource.MimeType.Should().Be("text/html;profile=mcp-app");
        AssertEmptyCsp(challengeResource.ProtocolResource.Meta);
        var challengeHtml = (await challengeResource.ReadAsync())
            .Contents.OfType<TextResourceContents>()
            .Should().ContainSingle().Subject.Text;
        challengeHtml.Should().Contain("Change Context Code Challenge");
        challengeHtml.Should().Contain("answer_change_context_challenge");
        challengeHtml.Should().Contain("record_change_context_challenge_note");
        challengeHtml.Should().NotContain("innerHTML");
        challengeHtml.Should().NotContain("Authorization");
        Encoding.UTF8.GetByteCount(challengeHtml).Should().BeLessThan(64 * 1024);
    }

    private static void AssertAppMetadata(
        object? metadata,
        string uri,
        params string[] expectedVisibility)
    {
        if (expectedVisibility.Length == 0)
            expectedVisibility = ["model", "app"];
        var json = JsonSerializer.SerializeToElement(
            metadata,
            ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        var ui = json.GetProperty("ui");
        ui.GetProperty("resourceUri").GetString().Should().Be(uri);
        ui.GetProperty("visibility").EnumerateArray()
            .Select(item => item.GetString())
            .Should().BeEquivalentTo(expectedVisibility);
    }

    private static void AssertEmptyCsp(object? metadata)
    {
        var json = JsonSerializer.SerializeToElement(
            metadata,
            ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        var csp = json.GetProperty("ui").GetProperty("csp");
        csp.GetProperty("connectDomains").GetArrayLength().Should().Be(0);
        csp.GetProperty("resourceDomains").GetArrayLength().Should().Be(0);
        csp.GetProperty("frameDomains").GetArrayLength().Should().Be(0);
        csp.GetProperty("baseUriDomains").GetArrayLength().Should().Be(0);
    }
}
