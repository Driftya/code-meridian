namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record IngestNodeRequest(
    string Id,
    string Name,
    string Type,
    string? Namespace,
    string? FilePath,
    int? LineNumber,
    string? Summary,
    int? LineCount = null,
    string? SourceSnippet = null,
    string? SourceHash = null);

internal sealed record IngestEdgeRequest(
    string SourceId,
    string TargetId,
    string RelationshipType,
    string? CallName = null,
    int? ParamCount = null,
    string? TargetName = null,
    string? TargetType = null,
    bool? IsAsync = null,
    string? CallSite = null,
    double? Confidence = null);

internal sealed record MethodCandidate(string Id, string? Namespace, string? FilePath, string Name, int ParameterCount);

internal sealed record TypeCandidate(string Id, string Type, string? Namespace, string? FilePath, string Name, string ShortName);

internal sealed class StringTupleComparer : IEqualityComparer<(string Name, int ParameterCount)>
{
    public static readonly StringTupleComparer Ordinal = new();
    public static readonly IEqualityComparer<(string Type, string Name)> OrdinalType =
        EqualityComparer<(string Type, string Name)>.Create(
            (x, y) => string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, y.Name, StringComparison.Ordinal),
            obj => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type),
                StringComparer.Ordinal.GetHashCode(obj.Name)));

    public bool Equals((string Name, int ParameterCount) x, (string Name, int ParameterCount) y) =>
        x.ParameterCount == y.ParameterCount && string.Equals(x.Name, y.Name, StringComparison.Ordinal);

    public int GetHashCode((string Name, int ParameterCount) obj) =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Name), obj.ParameterCount);
}
