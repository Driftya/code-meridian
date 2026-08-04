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

public sealed class McpTasksEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpTasksEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UsesTasksOnlyForKeywordMaintenance()
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
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var synchronous = await client.CallToolAsTaskAsync(new CallToolRequestParams
        {
            Name = "get_client_extension_contract"
        });
        synchronous.IsTask.Should().BeFalse();

        var taskCall = await client.CallToolAsTaskAsync(new CallToolRequestParams
        {
            Name = "rebuild_keyword_graph",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["projectContext"] = JsonSerializer.SerializeToElement("CodeMeridian")
            }
        });

        taskCall.IsTask.Should().BeTrue();
        taskCall.TaskCreated!.TimeToLive.Should().Be(TimeSpan.FromMinutes(30));
        taskCall.TaskCreated.PollIntervalMs.Should().Be(500);

        var completed = await WaitForTaskStatusAsync(
            client,
            taskCall.TaskCreated.TaskId,
            McpTaskStatus.Completed);
        var completedResult = completed.Should().BeOfType<CompletedTaskResult>().Subject;
        var toolResult = JsonSerializer.Deserialize<CallToolResult>(
            completedResult.Result,
            ModelContextProtocol.McpJsonUtilities.DefaultOptions);

        toolResult.Should().NotBeNull();
        toolResult!.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("Keyword graph rebuilt");
        await keywordGraph.Received(1).RebuildKeywordGraphAsync(
            "CodeMeridian",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TaskAwareClient_CanPollKeywordMaintenanceToFinalToolResult()
    {
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.ClassifyKeywordsAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("Keywords classified.");
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKeywordGraphService>();
                services.AddSingleton(keywordGraph);
            }));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams
            {
                Name = "classify_keywords",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["projectContext"] = JsonSerializer.SerializeToElement("CodeMeridian")
                }
            },
            10);

        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("Keywords classified");
    }

    [Fact]
    public async Task CanDisableTasksWithoutDisablingOrdinaryCalls()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Tasks:Enabled", "false"));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        (client.ServerCapabilities.Extensions?
            .ContainsKey("io.modelcontextprotocol/tasks") ?? false)
            .Should().BeFalse();

        var result = await client.CallToolAsync(
            "get_client_extension_contract",
            new Dictionary<string, object?>());
        result.Content.OfType<TextContentBlock>().Should().ContainSingle();
    }

    [Fact]
    public async Task CancelsKeywordMaintenanceTaskCooperatively()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keywordGraph = Substitute.For<IKeywordGraphService>();
        keywordGraph.ClassifyKeywordsAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var cancellationToken = callInfo.Arg<CancellationToken>();
                started.TrySetResult();
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKeywordGraphService>();
                services.AddSingleton(keywordGraph);
            }));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var taskCall = await client.CallToolAsTaskAsync(new CallToolRequestParams
        {
            Name = "classify_keywords",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["projectContext"] = JsonSerializer.SerializeToElement("CodeMeridian")
            }
        });
        taskCall.IsTask.Should().BeTrue();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await started.Task.WaitAsync(timeout.Token);
        await client.CancelTaskAsync(taskCall.TaskCreated!.TaskId, timeout.Token);
        await cancellationObserved.Task.WaitAsync(timeout.Token);

        var cancelled = await WaitForTaskStatusAsync(
            client,
            taskCall.TaskCreated.TaskId,
            McpTaskStatus.Cancelled,
            timeout.Token);
        cancelled.Should().BeOfType<CancelledTaskResult>();
    }

    private static async Task<GetTaskResult> WaitForTaskStatusAsync(
        McpClient client,
        string taskId,
        McpTaskStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var result = await client.GetTaskAsync(taskId, timeout.Token);
            if (result.Status == expectedStatus)
                return result;

            result.Status.Should().NotBe(
                McpTaskStatus.Cancelled,
                $"task {taskId} reached an unexpected terminal state");
            result.Status.Should().NotBe(
                McpTaskStatus.Failed,
                $"task {taskId} reached an unexpected terminal state");
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }
}
