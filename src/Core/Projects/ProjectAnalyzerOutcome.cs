namespace CodeMeridian.Core.Projects;

public sealed record ProjectAnalyzerOutcome(
    string Analyzer,
    string Scope,
    string State,
    string Coverage,
    string Mode,
    DateTimeOffset ObservedAt,
    string ReporterVersion,
    IReadOnlyList<string> Limitations)
{
    public ProjectAnalyzerFingerprint? Fingerprint { get; init; }
    public string? SourceFingerprint { get; init; }
}
