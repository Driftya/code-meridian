namespace CodeMeridian.Core.Projects;

public sealed record ProjectRepositoryRevision(string Commit, bool? Dirty)
{
    public string? Validate() => Commit is { Length: 40 or 64 } && Commit.All(char.IsAsciiHexDigit)
        ? null : "Repository revision must be a full Git commit hash.";
}
