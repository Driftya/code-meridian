namespace CodeMeridian.Application.Services;

public sealed record AnalysisConfigurationEntry(
    string CanonicalKey,
    string? Value);

public sealed record AnalysisConfigurationSourceResult(
    IReadOnlyList<AnalysisConfigurationEntry> Entries,
    IReadOnlyList<string> Warnings,
    string? SourceDescription = null);

public sealed record AnalysisOptionsResolutionMetadata(
    bool UsedGlobalConfig,
    bool UsedProjectConfig,
    IReadOnlyList<string> Warnings);

public sealed record ResolvedProjectAnalysisOptions(
    CodebaseAnalysisOptions Options,
    AnalysisOptionsResolutionMetadata Metadata);
