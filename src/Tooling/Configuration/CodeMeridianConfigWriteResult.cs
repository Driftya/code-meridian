namespace CodeMeridian.Tooling.Configuration;

public sealed record CodeMeridianConfigWriteResult(
    bool Created,
    bool Changed,
    string? BackupPath,
    int PreviousVersion,
    int CurrentVersion,
    IReadOnlyList<string> AddedPaths);
