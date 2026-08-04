using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;

namespace CodeMeridian.McpServer.Tests;

[Collection(McpLiveAcceptanceCollection.Name)]
[Trait("Category", "LiveAcceptance")]
public sealed class McpLiveAcceptanceTests
{
    private const string ModernProtocolVersion = "2026-07-28";
    private const string DownLevelProtocolVersion = "2025-11-25";
    private const string ProjectContext = "CodeMeridian";
    private static readonly HashSet<string> StructuredToolNames =
    [
        "find_connection",
        "get_client_extension_contract",
        "list_client_extension_examples",
        "get_client_extension_example"
    ];
    private readonly ITestOutputHelper _output;

    public McpLiveAcceptanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveFact]
    public async Task FreshModernClient_NegotiatesAndReadsIndexedGraph()
    {
        var recorder = new McpWireCaptureHandler
        {
            InnerHandler = new HttpClientHandler()
        };
        using var httpClient = CreateHttpClient(recorder);
        var connectTimer = Stopwatch.StartNew();
        await using var client = await CreateClientAsync(httpClient);
        connectTimer.Stop();

        var coldListTimer = Stopwatch.StartNew();
        var tools = await client.ListToolsAsync(new ListToolsRequestParams());
        coldListTimer.Stop();
        var warmListTimer = Stopwatch.StartNew();
        var warmTools = await client.ListToolsAsync(new ListToolsRequestParams());
        warmListTimer.Stop();
        var toolsExchange = recorder.Exchanges.Last(exchange =>
            exchange.RequestBody.Contains("\"tools/list\"", StringComparison.Ordinal));
        var toolsPayloadBytes = Encoding.UTF8.GetByteCount(toolsExchange.ResponseBody);

        client.NegotiatedProtocolVersion.Should().Be(ModernProtocolVersion);
        tools.Tools.Should().HaveCount(62);
        warmTools.Tools.Should().HaveCount(62);
        toolsPayloadBytes.Should().BeLessThan(512 * 1024);
        tools.TimeToLive.Should().Be(TimeSpan.FromMinutes(5));
        tools.CacheScope.Should().Be(CacheScope.Private);
        tools.Tools.Should().OnlyContain(tool =>
            tool.InputSchema.ValueKind == JsonValueKind.Object);
        tools.Tools.Should().OnlyContain(tool => tool.Annotations != null);
        tools.Tools.Where(tool => tool.OutputSchema != null)
            .Select(tool => tool.Name)
            .Should().BeEquivalentTo(StructuredToolNames);

        var freshness = await client.CallToolAsync(
            "check_graph_freshness",
            new Dictionary<string, object?> { ["projectContext"] = ProjectContext });

        freshness.IsError.Should().NotBeTrue();
        var freshnessText = freshness.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text;
        freshnessText.Should().NotBeNullOrWhiteSpace();

        _output.WriteLine(
            "Protocol={0}; connect={1}ms; tools/list cold={2}ms; tools/list warm={3}ms; tools/list payload={4} bytes",
            client.NegotiatedProtocolVersion,
            connectTimer.ElapsedMilliseconds,
            coldListTimer.ElapsedMilliseconds,
            warmListTimer.ElapsedMilliseconds,
            toolsPayloadBytes);
        _output.WriteLine(string.Join(
            Environment.NewLine,
            freshnessText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(4)));
    }

    [LiveFact]
    public async Task ModernClient_ValidatesEveryStructuredPilot()
    {
        using var httpClient = CreateHttpClient();
        await using var client = await CreateClientAsync(httpClient);
        var tools = await client.ListToolsAsync();
        var calls = new[]
        {
            new StructuredCall("find_connection", new Dictionary<string, object?>
            {
                ["fromId"] = "__live_acceptance_missing_source__",
                ["toId"] = "__live_acceptance_missing_target__"
            }),
            new StructuredCall("get_client_extension_contract", []),
            new StructuredCall("list_client_extension_examples", []),
            new StructuredCall("get_client_extension_example", new Dictionary<string, object?>
            {
                ["exampleId"] = "keyword-search"
            })
        };

        foreach (var call in calls)
        {
            var tool = tools.Single(tool => tool.Name == call.Name);
            var outputSchema = tool.ProtocolTool.OutputSchema;
            outputSchema.Should().NotBeNull($"{call.Name} advertises structured content");

            var result = await client.CallToolAsync(call.Name, call.Arguments);

            result.IsError.Should().NotBeTrue();
            result.Content.OfType<TextContentBlock>().Should().ContainSingle()
                .Which.Text.Should().NotBeNullOrWhiteSpace();
            result.StructuredContent.Should().NotBeNull();
            JsonSchema.FromText(outputSchema!.Value.GetRawText())
                .Evaluate(result.StructuredContent!.Value)
                .IsValid.Should().BeTrue($"{call.Name} must satisfy its advertised output schema");
        }
    }

    [LiveFact]
    public async Task DownLevelClient_RetainsUsefulTextResults()
    {
        using var httpClient = CreateHttpClient();
        await using var client = await CreateClientAsync(httpClient, DownLevelProtocolVersion);

        client.NegotiatedProtocolVersion.Should().Be(DownLevelProtocolVersion);
        (await client.ListToolsAsync()).Should().HaveCount(62);

        var result = await client.CallToolAsync(
            "get_client_extension_contract",
            new Dictionary<string, object?>());

        result.IsError.Should().NotBeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().StartWith("# Client Extension Contract");
    }

    [LiveFact]
    public async Task AppsCapability_IsInternallyConsistentWhenEnabledOrDisabled()
    {
        using var httpClient = CreateHttpClient();
        await using var client = await CreateClientAsync(httpClient);
        var tools = await client.ListToolsAsync();
        var hasAppsCapability = client.ServerCapabilities.Extensions?
            .ContainsKey("io.modelcontextprotocol/ui") ?? false;
        _output.WriteLine("MCP Apps capability enabled: {0}", hasAppsCapability);
        var appTools = tools.Where(tool =>
                tool.Name is "get_client_extension_contract" or "find_connection")
            .ToArray();

        if (!hasAppsCapability)
        {
            appTools.Should().OnlyContain(tool => tool.ProtocolTool.Meta == null);
            return;
        }

        appTools.Should().OnlyContain(tool => tool.ProtocolTool.Meta != null);
        var resources = await client.ListResourcesAsync();
        resources.Select(resource => resource.Uri).Should().Contain(
            "ui://code-meridian/client-extension-contract",
            "ui://code-meridian/connection-viewer");

        foreach (var resource in resources.Where(resource =>
                     resource.Uri.StartsWith("ui://code-meridian/", StringComparison.Ordinal)))
        {
            resource.MimeType.Should().Be("text/html;profile=mcp-app");
            var html = (await resource.ReadAsync()).Contents.OfType<TextResourceContents>()
                .Should().ContainSingle().Subject.Text;
            html.Should().NotContain("Authorization");
            html.Should().NotContain("CodeMeridian_Auth_ApiKey");
        }
    }

    [LiveFact(true)]
    public async Task TaskAwareClient_StartsAndPollsGraphMaintenance()
    {
        using var httpClient = CreateHttpClient();
        await using var client = await CreateClientAsync(httpClient);
        var timer = Stopwatch.StartNew();

        var result = await client.CallToolWithPollingAsync(
            CreateTaskRequest("classify_keywords"),
            10);
        timer.Stop();

        result.IsError.Should().NotBeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().NotBeNullOrWhiteSpace();
        _output.WriteLine("classify_keywords task completed in {0}ms", timer.ElapsedMilliseconds);
    }

    [LiveFact(true)]
    public async Task TaskAwareClient_CancelsGraphMaintenance()
    {
        using var httpClient = CreateHttpClient();
        await using var client = await CreateClientAsync(httpClient);
        var taskCall = await client.CallToolAsTaskAsync(CreateTaskRequest("rebuild_keyword_graph"));

        taskCall.IsTask.Should().BeTrue();
        taskCall.TaskCreated.Should().NotBeNull();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var timer = Stopwatch.StartNew();
        await client.CancelTaskAsync(taskCall.TaskCreated!.TaskId, timeout.Token);

        var final = await WaitForTerminalTaskAsync(client, taskCall.TaskCreated.TaskId, timeout.Token);
        timer.Stop();
        final.Status.Should().Be(McpTaskStatus.Cancelled);
        _output.WriteLine("rebuild_keyword_graph cancellation reached terminal state in {0}ms", timer.ElapsedMilliseconds);
    }

    private static CallToolRequestParams CreateTaskRequest(string toolName) =>
        new()
        {
            Name = toolName,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["projectContext"] = JsonSerializer.SerializeToElement(ProjectContext)
            }
        };

    private static async Task<GetTaskResult> WaitForTerminalTaskAsync(
        McpClient client,
        string taskId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await client.GetTaskAsync(taskId, cancellationToken);
            if (result.Status is McpTaskStatus.Completed
                or McpTaskStatus.Cancelled
                or McpTaskStatus.Failed)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler? handler = null) =>
        handler is null
            ? new HttpClient
            {
                BaseAddress = new Uri(GetRequiredEnvironmentVariable("CODEMERIDIAN_LIVE_URL")),
                Timeout = TimeSpan.FromMinutes(3)
            }
            : new HttpClient(handler)
            {
                BaseAddress = new Uri(GetRequiredEnvironmentVariable("CODEMERIDIAN_LIVE_URL")),
                Timeout = TimeSpan.FromMinutes(3)
            };

    private static Task<McpClient> CreateClientAsync(
        HttpClient httpClient,
        string? protocolVersion = null) =>
        McpTestClient.CreateAsync(
            httpClient,
            protocolVersion,
            GetRequiredEnvironmentVariable("CODEMERIDIAN_LIVE_API_KEY"));

    private static string GetRequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing required environment variable {name}.");

    private sealed record StructuredCall(
        string Name,
        Dictionary<string, object?> Arguments);
}
