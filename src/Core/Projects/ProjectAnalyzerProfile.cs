namespace CodeMeridian.Core.Projects;

public sealed record ProjectAnalyzerProfile(
    string Id,
    bool Supported,
    string ConfigurationState,
    string IndexingState,
    IReadOnlyList<string> SupportedCapabilities,
    IReadOnlyList<string> Limitations);
