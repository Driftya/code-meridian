namespace CodeMeridian.Tooling.Watching;

public sealed record WatchDebounceBatch(
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> DeletedFiles,
    bool ForceFullRescan = false)
{
    public bool IsEmpty => ChangedFiles.Count == 0 && DeletedFiles.Count == 0 && !ForceFullRescan;
}

