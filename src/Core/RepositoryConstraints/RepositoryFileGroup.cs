namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryFileGroup(
    string Id,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string>? FileKinds = null)
{
    public IReadOnlyList<string> FileKinds { get; init; } = FileKinds ?? [];
}
