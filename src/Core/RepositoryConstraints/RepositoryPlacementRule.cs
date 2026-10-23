using System.Text.Json.Serialization;

namespace CodeMeridian.Core.RepositoryConstraints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepositoryPlacementRule(
    string Id,
    string Group,
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> DeniedPaths,
    string Reason,
    string Severity,
    IReadOnlyList<RepositoryConstraintException> Exceptions)
{
    public IReadOnlyList<string> AllowedPaths { get; init; } = AllowedPaths ?? [];
    public IReadOnlyList<string> DeniedPaths { get; init; } = DeniedPaths ?? [];
    public IReadOnlyList<RepositoryConstraintException> Exceptions { get; init; } = Exceptions ?? [];
}
