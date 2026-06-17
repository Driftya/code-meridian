using Microsoft.Extensions.Logging;

namespace CodeMeridian.Tooling.Watching;

public sealed class IndexWatchLoop(
    DirectoryInfo rootPath,
    ILogger logger,
    TimeSpan? debounceDelay = null,
    Func<string, bool>? includePath = null)
{
    private readonly WatchBatchScheduler _scheduler = new(
        rootPath,
        logger,
        debounceDelay ?? TimeSpan.FromSeconds(2));

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

        watcher.Changed += (_, e) => ScheduleChange(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        watcher.Created += (_, e) => ScheduleChange(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        watcher.Deleted += (_, e) => ScheduleChange(e.FullPath, deleted: true, onBatchAsync, cancellationToken);
        watcher.Renamed += (_, e) =>
        {
            ScheduleChange(e.OldFullPath, deleted: true, onBatchAsync, cancellationToken);
            ScheduleChange(e.FullPath, deleted: false, onBatchAsync, cancellationToken);
        };
        watcher.Error += (_, e) =>
        {
            logger.LogWarning(e.GetException(), "[watch] File watcher failed; scheduling a full incremental rescan.");
            _scheduler.ScheduleFullRescan(onBatchAsync, cancellationToken);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        _scheduler.CancelPendingDebounce();
        logger.LogInformation("Watch mode stopped.");
    }

    private void ScheduleChange(
        string fullPath,
        bool deleted,
        Func<WatchDebounceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken cancellationToken)
    {
        if (includePath is not null && !includePath(fullPath))
            return;

        _scheduler.ScheduleChange(fullPath, deleted, onBatchAsync, cancellationToken);
    }
}
