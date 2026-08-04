using System.Text.Json;
using System.Diagnostics.Metrics;
using CodeMeridian.McpServer.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ModelContextProtocol.Extensions.Tasks;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpTaskStoreTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpTaskStoreTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConfiguredStore_EnforcesCapacityAndReleasesTerminalReservations()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Tasks:MaxActiveTasks", "1"));
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var first = await store.CreateTaskAsync();

        var rejected = () => store.CreateTaskAsync();
        await rejected.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*capacity of 1*");

        await store.SetCompletedAsync(first.TaskId, JsonSerializer.SerializeToElement(new { ok = true }));
        var next = await store.CreateTaskAsync();

        next.TaskId.Should().NotBe(first.TaskId);
        (await store.SetCancelledAsync(next.TaskId)).Should().BeTrue();
    }

    [Fact]
    public async Task ConfiguredStore_ConvertsOversizedCompletionToBoundedFailure()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Tasks:MaxResultBytes", "1024"));
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var task = await store.CreateTaskAsync();

        await store.SetCompletedAsync(
            task.TaskId,
            JsonSerializer.SerializeToElement(new { content = new string('x', 2048) }));
        var state = await store.GetTaskAsync(task.TaskId);

        state.Should().NotBeNull();
        state!.Status.Should().Be(McpTaskStatus.Failed);
        state.Error.Should().NotBeNull();
        state.Error!.Value.GetProperty("code").GetString().Should().Be("result_too_large");
    }

    [Fact]
    public async Task ConfiguredStore_TerminalTransitionsAreIdempotentAndLateCancellationIsRejected()
    {
        using var factory = _factory.WithWebHostBuilder(_ => { });
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var task = await store.CreateTaskAsync();
        var firstResult = JsonSerializer.SerializeToElement(new { value = 1 });
        var secondResult = JsonSerializer.SerializeToElement(new { value = 2 });

        await store.SetCompletedAsync(task.TaskId, firstResult);
        await store.SetCompletedAsync(task.TaskId, secondResult);
        var lateCancellation = await store.SetCancelledAsync(task.TaskId);
        var state = await store.GetTaskAsync(task.TaskId);

        lateCancellation.Should().BeFalse();
        state.Should().NotBeNull();
        state!.Status.Should().Be(McpTaskStatus.Completed);
        state.Result!.Value.GetProperty("value").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ConfiguredStore_RecordsProtocolFailureAsTerminalFailedState()
    {
        using var factory = _factory.WithWebHostBuilder(_ => { });
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var task = await store.CreateTaskAsync();
        var error = JsonSerializer.SerializeToElement(new { code = "protocol_failure" });

        await store.SetFailedAsync(task.TaskId, error);
        var state = await store.GetTaskAsync(task.TaskId);

        state.Should().NotBeNull();
        state!.Status.Should().Be(McpTaskStatus.Failed);
        state.Error!.Value.GetProperty("code").GetString().Should().Be("protocol_failure");
    }

    [Fact]
    public async Task InMemoryStore_RemovesTasksAfterTimeToLive()
    {
        var store = new InMemoryMcpTaskStore
        {
            DefaultTimeToLive = TimeSpan.FromMilliseconds(20)
        };
        var task = await store.CreateTaskAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(80));

        (await store.GetTaskAsync(task.TaskId)).Should().BeNull();
    }

    [Fact]
    public async Task HealthRegistration_ReportsBoundedProcessLocalStoreState()
    {
        using var factory = _factory.WithWebHostBuilder(_ => { });
        var health = factory.Services.GetRequiredService<HealthCheckService>();

        var report = await health.CheckHealthAsync(registration => registration.Name == "mcp_tasks");

        report.Status.Should().Be(HealthStatus.Healthy);
        var entry = report.Entries["mcp_tasks"];
        entry.Data["storage"].Should().Be("process-local-memory");
        entry.Data["maximumActiveTasks"].Should().Be(4);
        entry.Data["maximumResultBytes"].Should().Be(128 * 1024);
    }

    [Fact]
    public async Task TaskStore_ExportsActiveTaskGauge()
    {
        var observations = new List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CodeMeridian.McpServer.Tasks"
                && instrument.Name == "codemeridian.mcp.task.active")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "codemeridian.mcp.task.active")
                observations.Add(measurement);
        });
        listener.Start();

        using var factory = _factory.WithWebHostBuilder(_ => { });
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var task = await store.CreateTaskAsync();
        listener.RecordObservableInstruments();
        await store.SetCancelledAsync(task.TaskId);
        listener.RecordObservableInstruments();

        observations.Should().ContainInOrder(1, 0);
    }

    [Fact]
    public async Task TaskStore_ExportsBoundedDurationAndRejectionMetrics()
    {
        var durationOutcomes = new List<string?>();
        var rejectionReasons = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CodeMeridian.McpServer.Tasks")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "codemeridian.mcp.task.duration")
                durationOutcomes.Add(GetTag(tags, "mcp.task.outcome"));
        });
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "codemeridian.mcp.task.rejections")
                rejectionReasons.Add(GetTag(tags, "mcp.task.reason"));
        });
        listener.Start();

        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Tasks:MaxActiveTasks", "1"));
        var store = factory.Services.GetRequiredService<IMcpTaskStore>();
        var task = await store.CreateTaskAsync();
        var rejected = () => store.CreateTaskAsync();
        await rejected.Should().ThrowAsync<InvalidOperationException>();
        await store.SetCancelledAsync(task.TaskId);

        rejectionReasons.Should().Contain("capacity");
        rejectionReasons.Should().OnlyContain(reason =>
            reason == "capacity" || reason == "result_too_large");
        durationOutcomes.Should().Contain("cancelled");
        durationOutcomes.Should().OnlyContain(outcome =>
            outcome == "completed"
            || outcome == "failed"
            || outcome == "cancelled"
            || outcome == "expired");
    }

    private static string? GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == name)
                return tag.Value?.ToString();
        }

        return null;
    }
}
