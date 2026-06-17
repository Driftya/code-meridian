using Microsoft.Extensions.Logging;

namespace CodeMeridian.Tooling.Watching;

public sealed class WatchBatchScheduler(
    DirectoryInfo rootPath,
    ILogger logger,
    TimeSpan debounceDelay)
{
    private readonly object _gate = new();
    private readonly WatchDebounceBuffer _buffer = new(rootPath);
    private CancellationTokenSource? _debounceCts;
    private bool _processing;
    private bool _processAgain;

    public void ScheduleChange(
        string fullPath,
        bool deleted,
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        _buffer.RecordChange(fullPath, deleted);
        Schedule(onBatchAsync, cancellationToken);
    }

    public void ScheduleFullRescan(
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        _buffer.RecordFullRescan();
        Schedule(onBatchAsync, cancellationToken);
    }

    public WatchDebounceBatch Drain() => _buffer.Drain();

    public void CancelPendingDebounce()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _debounceCts;
            _debounceCts = null;
        }

        previous?.Cancel();
        previous?.Dispose();
    }

    private void Schedule(
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;

        lock (_gate)
        {
            previous = _debounceCts;
            current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _debounceCts = current;
        }

        previous?.Cancel();
        previous?.Dispose();

        _ = RunAfterDebounceAsync(current, onBatchAsync, cancellationToken);
    }

    private async Task RunAfterDebounceAsync(
        CancellationTokenSource debounceCts,
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(debounceDelay, debounceCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ProcessAsync(debounceCts, onBatchAsync, cancellationToken);
    }

    private async Task ProcessAsync(
        CancellationTokenSource debounceCts,
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_debounceCts, debounceCts))
                return;

            _debounceCts = null;

            if (_processing)
            {
                _processAgain = true;
                debounceCts.Dispose();
                return;
            }

            _processing = true;
        }

        debounceCts.Dispose();

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = Drain();
            if (!batch.IsEmpty)
            {
                logger.LogInformation("[watch] Change detected - re-indexing...");

                try
                {
                    await onBatchAsync(batch, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "[watch] Re-index failed.");
                }
            }

            lock (_gate)
            {
                if (_processAgain)
                {
                    _processAgain = false;
                    continue;
                }

                _processing = false;
                return;
            }
        }

        lock (_gate)
        {
            _processing = false;
        }
    }
}
