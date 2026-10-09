namespace CodeMeridian.Core.Projects;

public sealed record ProjectAnalysisRoot(
    string Kind,
    string Path,
    string? EvidencePath);
