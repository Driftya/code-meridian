namespace CodeMeridian.Indexer.Cli.Configuration;

internal sealed record ConfigurationEntryRecord(
    string RelativePath,
    string Format,
    string SourceKind,
    string RawKey,
    string CanonicalKey,
    string ValueType,
    string ValuePreview,
    bool IsSecretLike);
