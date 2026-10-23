using System.Text.Json.Serialization;

namespace CodeMeridian.Core.RepositoryConstraints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkdownReferenceRule(
    string Id,
    string SourceGroup,
    IReadOnlyList<string> AllowedTargetGroups,
    bool RequireLocalResolution,
    IReadOnlyList<string> ReferenceKinds,
    string Reason,
    string Severity,
    IReadOnlyList<RepositoryConstraintException> Exceptions)
{
    public IReadOnlyList<string> AllowedTargetGroups { get; init; } = AllowedTargetGroups ?? [];
    public IReadOnlyList<RepositoryConstraintException> Exceptions { get; init; } = Exceptions ?? [];
}
