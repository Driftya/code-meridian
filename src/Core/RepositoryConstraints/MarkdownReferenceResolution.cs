namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record MarkdownReferenceResolution(
    MarkdownReferenceFact Reference,
    string Status,
    string FragmentStatus,
    string Reason);
