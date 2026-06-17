namespace CodeMeridian.Tooling.Watching;

public sealed class WatchDebounceBuffer(DirectoryInfo rootPath)
{
    private readonly object _gate = new();
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
    private bool _forceFullRescan;

    public void RecordChange(string fullPath, bool deleted)
    {
        var relativePath = NormalizeRelativePath(rootPath, fullPath);

        lock (_gate)
        {
            if (deleted)
            {
                _changed.Remove(relativePath);
                _deleted.Add(relativePath);
            }
            else
            {
                _deleted.Remove(relativePath);
                _changed.Add(relativePath);
            }
        }
    }

    public void RecordFullRescan()
    {
        lock (_gate)
        {
            _forceFullRescan = true;
        }
    }

    public WatchDebounceBatch Drain()
    {
        lock (_gate)
        {
            var batch = new WatchDebounceBatch(
                _changed.ToArray(),
                _deleted.ToArray(),
                _forceFullRescan);

            _changed.Clear();
            _deleted.Clear();
            _forceFullRescan = false;
            return batch;
        }
    }

    public static string NormalizeRelativePath(DirectoryInfo rootPath, string fullPath) =>
        Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
}

