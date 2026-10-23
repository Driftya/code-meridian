namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record MarkdownReferenceEvaluation(
    RepositoryConstraintResult Result,
    IReadOnlyList<MarkdownReferenceResolution> References);
