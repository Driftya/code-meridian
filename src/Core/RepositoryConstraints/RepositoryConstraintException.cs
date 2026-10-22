namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintException(string Path, string Reason);
