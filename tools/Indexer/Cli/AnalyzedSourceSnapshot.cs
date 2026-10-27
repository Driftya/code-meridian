using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Core.Projects;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class AnalyzedSourceSnapshot
{
    private readonly DirectoryInfo _root;
    private readonly IReadOnlyList<ProjectInventoryFile> _files;
    private readonly Dictionary<string, string> _hashes;
    private readonly HashSet<string> _readDocuments = new(StringComparer.Ordinal);
    private bool _invalid;
    private IReadOnlyList<string>? _constraintExclusions;
    private bool? _constraintCaseSensitive;

    private AnalyzedSourceSnapshot(DirectoryInfo root, IReadOnlyList<ProjectInventoryFile> files,
        Dictionary<string, string> hashes)
    {
        _root = root;
        _files = files;
        _hashes = hashes;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("codemeridian-source/1\n"u8);
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes($"{file.Path}\0{file.Kind}\0{hashes[file.Path]}\n"));
        Fingerprint = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public string Fingerprint { get; }
    public string FileFingerprint(string path) => "sha256:" + _hashes[path].ToLowerInvariant();
    public void BindConstraintInventory(RepositoryFileInventory inventory, bool caseSensitive)
    {
        _constraintExclusions = inventory.ExcludedPaths;
        _constraintCaseSensitive = caseSensitive;
    }
    public bool AllDocumentsRead => _files.Where(file => file.Kind is "markdown" or "text")
        .All(file => _readDocuments.Contains(file.Path));

    public static AnalyzedSourceSnapshot Capture(DirectoryInfo root, IReadOnlyList<ProjectInventoryFile> files,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = OpenSource(root, file.Path);
            hashes.Add(file.Path, Convert.ToHexString(SHA256.HashData(stream)));
        }
        var snapshot = new AnalyzedSourceSnapshot(root, files, hashes);
        if (!snapshot.IsCurrent(cancellationToken)) throw new IOException("Source changed while capturing analysis inputs.");
        return snapshot;
    }

    public bool IsCurrent(CancellationToken cancellationToken)
    {
        if (_invalid) return false;
        try
        {
            var inventory = new RepositoryInventoryDiscovery().Discover(_root, cancellationToken,
                _files.Where(file => IndexingExclusionPolicy.IsIgnoredRelativePath(file.Path)).Select(file => file.Path).ToArray());
            if (!inventory.IsComplete || !inventory.Files.SequenceEqual(_hashes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                return Invalidate();
            if (_constraintExclusions is not null && (!inventory.ExcludedPaths.SequenceEqual(_constraintExclusions, StringComparer.Ordinal)
                || DirectoryCaseSensitivity.Read(_root, inventory.Directories, cancellationToken) != _constraintCaseSensitive))
                return Invalidate();
            foreach (var file in _files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = OpenSource(_root, file.Path);
                if (Convert.ToHexString(SHA256.HashData(stream)) != _hashes[file.Path]) return Invalidate();
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Invalidate(); }
    }

    public Task<string> ReadSourceAsync(FileInfo file, CancellationToken cancellationToken)
        => ReadSourceAsync(file, true, cancellationToken);

    public Task<string> ReadStructuralSourceAsync(FileInfo file, CancellationToken cancellationToken)
        => ReadSourceAsync(file, false, cancellationToken);

    private async Task<string> ReadSourceAsync(FileInfo file, bool searchableDocumentRead, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.GetRelativePath(_root.FullName, file.FullName).Replace('\\', '/');
            if (_invalid || !_hashes.ContainsKey(path))
                throw new IOException("Source is not part of the captured analysis inputs.");
            await using var stream = OpenSource(_root, path);
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, cancellationToken);
            if (Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))) != _hashes[path])
                throw new IOException("Source bytes differ from the captured analysis inputs.");
            bytes.Position = 0;
            using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            if (searchableDocumentRead) _readDocuments.Add(path);
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Invalidate();
            throw;
        }
    }

    private bool Invalidate() { _invalid = true; return false; }

    private static FileStream OpenSource(DirectoryInfo root, string path)
    {
        path = ProjectProfilePath.Normalize(path);
        var file = new FileInfo(Path.Combine(root.FullName, path));
        for (FileSystemInfo? entry = file; entry is not null; entry = entry is FileInfo source ? source.Directory : ((DirectoryInfo)entry).Parent)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Analysis inputs cannot follow symbolic links or junctions.");
            if (entry.FullName == root.FullName) break;
        }
        return file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
