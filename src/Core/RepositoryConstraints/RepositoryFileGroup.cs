using System.Text.Json.Serialization;

namespace CodeMeridian.Core.RepositoryConstraints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepositoryFileGroup(
    string Id,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string>? FileKinds = null)
{
    public IReadOnlyList<string> FileKinds { get; init; } = FileKinds ?? [];
}
