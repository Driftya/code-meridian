namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed record ProducerFileOutcome(string Path, string? SourceFingerprint, string Status,
    IReadOnlyList<string> Diagnostics, string? Dialect = null, string? DatabaseScope = null);
