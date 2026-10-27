namespace CodeMeridian.Tooling.Discovery;

public sealed class RepositoryInventoryDiscovery
{
    private const int MaximumWarnings = 20;

    public RepositoryFileInventory Discover(DirectoryInfo root, CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? admittedPaths = null)
    {
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {root.FullName}");

        var files = new List<string>();
        var warnings = new List<string>();
        var excluded = new List<string>();
        var directories = new List<string>();
        var admitted = (admittedPaths ?? []).ToHashSet(StringComparer.Ordinal);
        if (admitted.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 2048 || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':')
            || path.Split('/').Any(segment => segment is "" or "." or "..") || path.Any(char.IsControl)))
            throw new ArgumentException("Admitted files must use canonical repository-relative paths.", nameof(admittedPaths));
        var warningCount = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Warn(RelativePath(directory), "symbolic link or junction was not followed");
                    continue;
                }
                directories.Add(RelativePath(directory));

                foreach (var entry in directory.GetFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = RelativePath(entry);
                    if (IndexingExclusionPolicy.IsIgnoredPath(root, entry)
                        && !admitted.Contains(path) && !(entry is DirectoryInfo && admitted.Any(file => file.StartsWith(path + "/", StringComparison.Ordinal))))
                    {
                        excluded.Add(path);
                        continue;
                    }

                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Warn(RelativePath(entry), "symbolic link or junction was not followed");
                            continue;
                        }

                        if (entry is DirectoryInfo child)
                            pending.Push(child);
                        else if (entry is FileInfo)
                            files.Add(RelativePath(entry));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Warn(RelativePath(entry), "entry could not be inspected");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn(RelativePath(directory), "directory could not be enumerated");
            }
        }

        files.Sort(StringComparer.Ordinal);
        warnings.Sort(StringComparer.Ordinal);
        return new RepositoryFileInventory(files.ToArray(), warnings.ToArray(), warningCount)
        {
            ExcludedPaths = excluded.Order(StringComparer.Ordinal).ToArray(),
            Directories = directories.Order(StringComparer.Ordinal).ToArray()
        };

        string RelativePath(FileSystemInfo entry) =>
            Path.GetRelativePath(root.FullName, entry.FullName).Replace('\\', '/');

        void Warn(string path, string reason)
        {
            warningCount++;
            if (warnings.Count < MaximumWarnings)
                warnings.Add($"{path}: {reason}.");
        }
    }
}
