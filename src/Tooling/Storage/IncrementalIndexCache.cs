using System.Security.Cryptography;
using System.Text.Json;

namespace CodeMeridian.Tooling.Storage;

public sealed class IncrementalIndexCache
{
    private const int CacheVersion = 3;
    private readonly FileInfo _cacheFile;
    private readonly CacheState _state;

    private IncrementalIndexCache(FileInfo cacheFile, CacheState state)
    {
        _cacheFile = cacheFile;
        _state = state;
    }

    public static IncrementalIndexCache Load(DirectoryInfo cacheDirectory, string projectContext)
    {
        var cacheFile = new FileInfo(Path.Combine(cacheDirectory.FullName, $"indexer-files-{Hash(projectContext)}.json"));

        if (!cacheFile.Exists)
            return new IncrementalIndexCache(cacheFile, new CacheState());

        try
        {
            var state = JsonSerializer.Deserialize<CacheState>(File.ReadAllText(cacheFile.FullName)) ?? new CacheState();
            return state.Version == CacheVersion
                ? new IncrementalIndexCache(cacheFile, state)
                : new IncrementalIndexCache(cacheFile, new CacheState());
        }
        catch
        {
            return new IncrementalIndexCache(cacheFile, new CacheState());
        }
    }

    public IncrementalIndexPlan BuildPlan(
        DirectoryInfo root,
        IReadOnlyCollection<FileInfo> files,
        bool forceFull,
        Func<string, bool>? isPathInScope = null)
    {
        var current = files
            .Select(file => CacheEntry.FromFile(root, file))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var retained = isPathInScope is null
            ? []
            : _state.Files.Where(entry => !isPathInScope(entry.Path)).ToArray();
        var nextState = current
            .Concat(retained)
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (forceFull || _state.Files.Count == 0)
        {
            return new IncrementalIndexPlan(
                [.. current.Select(entry => entry.Path)],
                [],
                nextState);
        }

        var previous = _state.Files
            .Where(entry => isPathInScope is null || isPathInScope(entry.Path))
            .ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var currentByPath = current.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);

        var changed = current
            .Where(entry => !previous.TryGetValue(entry.Path, out var old) || !entry.SameContentAs(old))
            .Select(entry => entry.Path)
            .ToArray();

        var deleted = previous.Keys
            .Where(path => !currentByPath.ContainsKey(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IncrementalIndexPlan(changed, deleted, nextState);
    }

    public void Save(IncrementalIndexPlan plan)
    {
        _cacheFile.Directory?.Create();
        var state = new CacheState
        {
            Version = CacheVersion,
            Files = [.. plan.NextState]
        };

        File.WriteAllText(
            _cacheFile.FullName,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private sealed class CacheState
    {
        public int Version { get; set; } = CacheVersion;
        public List<CacheEntry> Files { get; set; } = [];
    }

    public sealed record CacheEntry(string Path, long LastWriteUtcTicks, long Length, string ContentHash)
    {
        public static CacheEntry FromFile(DirectoryInfo root, FileInfo file) =>
            new(
                System.IO.Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/'),
                file.LastWriteTimeUtc.Ticks,
                file.Length,
                HashFile(file));

        public bool SameContentAs(CacheEntry other) =>
            string.Equals(ContentHash, other.ContentHash, StringComparison.OrdinalIgnoreCase);

        private static string HashFile(FileInfo file)
        {
            using var stream = file.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}

public sealed record IncrementalIndexPlan(
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<IncrementalIndexCache.CacheEntry> NextState)
{
    public bool HasChanges => ChangedFiles.Count > 0 || DeletedFiles.Count > 0;
}
