namespace CodeMeridian.Application.Services;

public sealed record MinimalContextExplainedFileResult(
    string FilePath,
    string Reason,
    string EvidencePath,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> NearbyTests);
