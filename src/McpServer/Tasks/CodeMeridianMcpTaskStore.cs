using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using CodeMeridian.McpServer.Configuration;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace CodeMeridian.McpServer.Tasks;

internal sealed class CodeMeridianMcpTaskStore : IMcpTaskStore, IDisposable
{
    private readonly InMemoryMcpTaskStore _inner;
    private readonly McpTaskRuntimeOptions _options;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeTasks = new(StringComparer.Ordinal);
    private readonly Meter _meter = new("CodeMeridian.McpServer.Tasks");
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _rejections;
    private readonly ObservableGauge<int> _activeGauge;
    private int _activeReservations;

    public CodeMeridianMcpTaskStore(McpTaskRuntimeOptions options)
    {
        _options = options;
        _inner = new InMemoryMcpTaskStore
        {
            DefaultPollIntervalMs = options.BoundedPollIntervalMilliseconds,
            DefaultTimeToLive = options.TimeToLive
        };
        _duration = _meter.CreateHistogram<double>(
            "codemeridian.mcp.task.duration",
            unit: "ms",
            description: "MCP task duration by bounded terminal outcome");
        _rejections = _meter.CreateCounter<long>(
            "codemeridian.mcp.task.rejections",
            description: "MCP task creations rejected by bounded reason");
        _activeGauge = _meter.CreateObservableGauge(
            "codemeridian.mcp.task.active",
            ObserveActiveTasks,
            description: "MCP tasks that have not reached a terminal state");
    }

    public int ActiveTaskCount
    {
        get
        {
            ReclaimExpiredReservations();
            return Math.Max(0, Volatile.Read(ref _activeReservations));
        }
    }

    public int MaximumActiveTasks => _options.BoundedMaxActiveTasks;
    public TimeSpan TimeToLive => _options.TimeToLive;
    public int MaximumResultBytes => _options.BoundedMaxResultBytes;

    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived
    {
        add => _inner.InputResponseReceived += value;
        remove => _inner.InputResponseReceived -= value;
    }

    public async Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        ReclaimExpiredReservations();
        if (Interlocked.Increment(ref _activeReservations) > _options.BoundedMaxActiveTasks)
        {
            Interlocked.Decrement(ref _activeReservations);
            _rejections.Add(1, new KeyValuePair<string, object?>("mcp.task.reason", "capacity"));
            throw new InvalidOperationException(
                $"The MCP task capacity of {_options.BoundedMaxActiveTasks} active tasks has been reached.");
        }

        try
        {
            var task = await _inner.CreateTaskAsync(cancellationToken);
            _activeTasks[task.TaskId] = DateTimeOffset.UtcNow;
            return task;
        }
        catch
        {
            Interlocked.Decrement(ref _activeReservations);
            throw;
        }
    }

    public async Task<McpTaskInfo?> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _inner.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
            CompleteReservation(taskId, "expired");
        return task;
    }

    public async Task SetCompletedAsync(
        string taskId,
        JsonElement result,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(result.GetRawText()) > _options.BoundedMaxResultBytes)
        {
            _rejections.Add(1, new KeyValuePair<string, object?>("mcp.task.reason", "result_too_large"));
            var error = JsonSerializer.SerializeToElement(new
            {
                code = "result_too_large",
                message = $"The task result exceeded the {_options.BoundedMaxResultBytes}-byte limit."
            });
            await _inner.SetFailedAsync(taskId, error, cancellationToken);
            CompleteReservation(taskId, "failed");
            return;
        }

        await _inner.SetCompletedAsync(taskId, result, cancellationToken);
        CompleteReservation(taskId, "completed");
    }

    public async Task SetFailedAsync(
        string taskId,
        JsonElement error,
        CancellationToken cancellationToken = default)
    {
        await _inner.SetFailedAsync(taskId, error, cancellationToken);
        CompleteReservation(taskId, "failed");
    }

    public async Task<bool> SetCancelledAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var cancelled = await _inner.SetCancelledAsync(taskId, cancellationToken);
        if (cancelled)
            CompleteReservation(taskId, "cancelled");
        return cancelled;
    }

    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default) =>
        _inner.ResolveInputRequestsAsync(taskId, inputResponses, cancellationToken);

    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default) =>
        _inner.SetInputRequestsAsync(taskId, inputRequests, cancellationToken);

    public void Dispose() => _meter.Dispose();

    private Measurement<int> ObserveActiveTasks() =>
        new(ActiveTaskCount);

    private void ReclaimExpiredReservations()
    {
        var cutoff = DateTimeOffset.UtcNow - _options.TimeToLive;
        foreach (var activeTask in _activeTasks)
        {
            if (activeTask.Value <= cutoff)
                CompleteReservation(activeTask.Key, "expired");
        }
    }

    private void CompleteReservation(string taskId, string outcome)
    {
        if (!_activeTasks.TryRemove(taskId, out var startedAt))
            return;

        Interlocked.Decrement(ref _activeReservations);
        _duration.Record(
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            new KeyValuePair<string, object?>("mcp.task.outcome", outcome));
    }
}
