namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintFinding(
    string Path,
    string Status,
    string Reason,
    int? Line = null,
    int? Column = null);
