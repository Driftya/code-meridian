using System.Threading.Channels;

namespace CodeMeridian.McpServer.Keywording;

public interface IKeywordRefreshQueue
{
    ValueTask QueueAsync(KeywordRefreshWorkItem item, CancellationToken cancellationToken = default);
}

public sealed record KeywordRefreshWorkItem(
    string SourceNodeId,
    string? ProjectContext);

public sealed class KeywordRefreshQueueOptions
{
    public int Capacity { get; init; } = 1_000;
    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromSeconds(2);
}

internal sealed class BackgroundKeywordRefreshQueue : IKeywordRefreshQueue
{
    private readonly Channel<KeywordRefreshWorkItem> _queue;

    public BackgroundKeywordRefreshQueue(Microsoft.Extensions.Options.IOptions<KeywordRefreshQueueOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Capacity);
        _queue = Channel.CreateBounded<KeywordRefreshWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<KeywordRefreshWorkItem> Reader => _queue.Reader;

    public ValueTask QueueAsync(KeywordRefreshWorkItem item, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(item.SourceNodeId)
            ? ValueTask.CompletedTask
            : _queue.Writer.WriteAsync(item, cancellationToken);
}

