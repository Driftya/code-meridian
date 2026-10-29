using CodeMeridian.Core.Projects;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed record ProducerExecutionReport(string ContractVersion, string RequestId, string Analyzer,
    string Project, string Scope, ProjectAnalyzerFingerprint Fingerprint, bool Published,
    IReadOnlyList<ProducerFileOutcome> Files, IReadOnlyDictionary<string, string> Capabilities,
    IReadOnlyList<string> Limitations, long? Generation = null, string? SourceFingerprint = null);
