using System.Text.Json;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpTaskRuntimeEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpTaskRuntimeEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OversizedTaskResult_CompletesWithBoundedToolError()
    {
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(new string('x', 2048));
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mcp:Tasks:MaxResultBytes", "1024");
            builder.ConfigureServices(services => ReplaceKeywordService(services, keywordGraph));
        });
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await ExecuteTaskAsync(client, "rebuild_keyword_graph");

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("1024-byte limit");
    }

    [Fact]
    public async Task DurationLimit_CancelsWorkAndCompletesWithToolError()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.ClassifyKeywordsAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var cancellationToken = callInfo.Arg<CancellationToken>();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return "Unexpected completion.";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
            });
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mcp:Tasks:MaxDurationSeconds", "1");
            builder.ConfigureServices(services => ReplaceKeywordService(services, keywordGraph));
        });
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await ExecuteTaskAsync(client, "classify_keywords");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await cancellationObserved.Task.WaitAsync(timeout.Token);
        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("1-second duration limit");
    }

    [Fact]
    public async Task ToolException_CompletesWithSanitizedToolError()
    {
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(
                new InvalidOperationException("controlled task failure")));
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => ReplaceKeywordService(services, keywordGraph)));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await ExecuteTaskAsync(client, "rebuild_keyword_graph");

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("An error occurred invoking 'rebuild_keyword_graph'.");
    }

    private static void ReplaceKeywordService(
        IServiceCollection services,
        IKeywordGraphService keywordGraph)
    {
        services.RemoveAll<IKeywordGraphService>();
        services.AddSingleton(keywordGraph);
    }

    private static async Task<CallToolResult> ExecuteTaskAsync(McpClient client, string toolName)
    {
        var taskCall = await client.CallToolAsTaskAsync(new CallToolRequestParams
        {
            Name = toolName,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["projectContext"] = JsonSerializer.SerializeToElement("CodeMeridian")
            }
        });
        var completed = await WaitForTaskStatusAsync(
            client,
            taskCall.TaskCreated!.TaskId,
            McpTaskStatus.Completed);
        var result = JsonSerializer.Deserialize<CallToolResult>(
            completed.Should().BeOfType<CompletedTaskResult>().Subject.Result,
            ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private static async Task<GetTaskResult> WaitForTaskStatusAsync(
        McpClient client,
        string taskId,
        McpTaskStatus expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await client.GetTaskAsync(taskId, timeout.Token);
            if (result.Status == expectedStatus)
                return result;

            result.Status.Should().NotBe(McpTaskStatus.Cancelled);
            result.Status.Should().NotBe(McpTaskStatus.Failed);
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }
}
