namespace CodeMeridian.Tooling.Discovery;

public sealed class RepositoryInventoryDiscovery
{
    private const int MaximumWarnings = 20;

    public RepositoryFileInventory Discover(DirectoryInfo root, CancellationToken cancellationToken = default)
    {
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {root.FullName}");

        var files = new List<string>();
        var warnings = new List<string>();
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

                foreach (var entry in directory.GetFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IndexingExclusionPolicy.IsIgnoredPath(root, entry))
                        continue;

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
        return new RepositoryFileInventory(files.ToArray(), warnings.ToArray(), warningCount);

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
