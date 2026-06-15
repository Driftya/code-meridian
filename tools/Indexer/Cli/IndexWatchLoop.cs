using Microsoft.Extensions.Logging;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class IndexWatchLoop(
    DirectoryInfo rootPath,
    ILogger logger)
{
    private readonly WatchDebounceBuffer _buffer = new(rootPath);
    private System.Timers.Timer? _debounceTimer;

    public async Task RunAsync(
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Watch mode active - monitoring {Path} for source, documentation, and configuration changes. Press Ctrl+C to exit.",
            rootPath.FullName);

        using var watcher = new FileSystemWatcher(rootPath.FullName)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        watcher.Changed += (_, e) => ScheduleReindex(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        watcher.Created += (_, e) => ScheduleReindex(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        watcher.Deleted += (_, e) => ScheduleReindex(e.FullPath, deleted: true, onBatchAsync, cancellationToken);
        watcher.Renamed += (_, e) =>
        {
            ScheduleReindex(e.OldFullPath, deleted: true, onBatchAsync, cancellationToken);
            ScheduleReindex(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("Watch mode stopped.");
    }

    internal void RecordChange(string fullPath, bool deleted) => _buffer.RecordChange(fullPath, deleted);

    internal WatchDebounceBatch Drain() => _buffer.Drain();

    private void ScheduleReindex(
        string fullPath,
        bool deleted,
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        RecordChange(fullPath, deleted);

        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Timers.Timer(2_000) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) =>
        {
            logger.LogInformation("[watch] Change detected - re-indexing...");
            var batch = Drain();

            try
            {
                await onBatchAsync(batch, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[watch] Re-index failed.");
            }
        };
        _debounceTimer.Start();
    }
}

internal sealed class WatchDebounceBuffer(DirectoryInfo rootPath)
{
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);

    public void RecordChange(string fullPath, bool deleted)
    {
        var relativePath = NormalizeRelativePath(rootPath, fullPath);
        if (deleted)
            _deleted.Add(relativePath);
        else
            _changed.Add(relativePath);
    }

    public WatchDebounceBatch Drain()
    {
        var batch = new WatchDebounceBatch(_changed.ToArray(), _deleted.ToArray());
        _changed.Clear();
        _deleted.Clear();
        return batch;
    }

    internal static string NormalizeRelativePath(DirectoryInfo rootPath, string fullPath) =>
        Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
}

internal sealed record WatchDebounceBatch(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> DeletedFiles);
