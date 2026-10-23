namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record MarkdownReferenceFact(
    string Kind,
    MarkdownReferenceSpan SourceSpan,
    MarkdownReferenceSpan TargetSpan,
    string RawTarget,
    string Target,
    string TargetKind,
    string? NormalizedPath,
    string? Fragment);
