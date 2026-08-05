namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record EdgeResolutionResult(
    List<IngestEdgeRequest> Edges,
    int UniqueResolvedEdges,
    RelationshipResolutionStats Stats)
{
    public int Attempted => Stats.Attempted;
    public int Resolved => UniqueResolvedEdges;
    public IReadOnlyDictionary<string, int> UnresolvedByReason => Stats.LegacyUnresolvedByReason;
}

public enum RelationshipResolutionDisposition
{
    ResolvedLocal,
    ExternalOrUnindexed,
    UnresolvedLocal,
    Indeterminate
}

public sealed record RelationshipResolutionSample(
    string EdgeKind,
    string Disposition,
    string Reason,
    string SourceId,
    string? FilePath,
    string? FileRole,
    int? LineNumber,
    string? TargetName,
    int? ParameterCount,
    string? ReceiverKind,
    string? ReceiverTypeHint);

public sealed record RelationshipResolutionStats(
    int Attempted,
    int ResolvedLocal,
    int ExternalOrUnindexed,
    int UnresolvedLocal,
    int Indeterminate,
    int DuplicateEdges,
    int SyntheticEdges,
    int UniqueResolvedEdges,
    IReadOnlyDictionary<string, int> Reasons,
    IReadOnlyDictionary<string, int> LegacyUnresolvedByReason,
    IReadOnlyDictionary<string, int> FailureCountsByFileRole,
    IReadOnlyList<RelationshipResolutionSample> Samples)
{
    public bool HasValidAccounting =>
        Attempted == ResolvedLocal + ExternalOrUnindexed + UnresolvedLocal + Indeterminate;
}

internal sealed class RelationshipResolutionCollector(string edgeKind, int sampleLimitPerReason = 3)
{
    private readonly Dictionary<string, int> _reasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _legacyUnresolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _failureCountsByFileRole = new(StringComparer.Ordinal);
    private readonly List<RelationshipResolutionSample> _samples = [];
    private readonly HashSet<string> _resolvedEdgeIds = new(StringComparer.Ordinal);
    private int _attempted;
    private int _resolvedLocal;
    private int _externalOrUnindexed;
    private int _unresolvedLocal;
    private int _indeterminate;
    private int _duplicates;

    public void RecordResolved(IngestNodeRequest? source, IngestEdgeRequest resolvedEdge)
    {
        _attempted++;
        _resolvedLocal++;
        if (!_resolvedEdgeIds.Add(BuildEdgeIdentity(resolvedEdge)))
            _duplicates++;
    }

    public void Record(
        RelationshipResolutionDisposition disposition,
        string reason,
        IngestNodeRequest? source,
        IngestEdgeRequest edge)
    {
        _attempted++;
        switch (disposition)
        {
            case RelationshipResolutionDisposition.ExternalOrUnindexed:
                _externalOrUnindexed++;
                break;
            case RelationshipResolutionDisposition.UnresolvedLocal:
                _unresolvedLocal++;
                break;
            case RelationshipResolutionDisposition.Indeterminate:
                _indeterminate++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
        }

        var dispositionName = ToMetadataName(disposition);
        var reasonKey = $"{dispositionName}:{reason}";
        _reasons[reasonKey] = _reasons.GetValueOrDefault(reasonKey) + 1;
        _legacyUnresolved[reason] = _legacyUnresolved.GetValueOrDefault(reason) + 1;

        if (disposition is RelationshipResolutionDisposition.UnresolvedLocal or RelationshipResolutionDisposition.Indeterminate)
        {
            var role = source?.Properties is not null && source.Properties.TryGetValue("fileRole", out var sourceRole)
                ? sourceRole
                : "Unknown";
            var roleKey = $"{dispositionName}:{role}";
            _failureCountsByFileRole[roleKey] = _failureCountsByFileRole.GetValueOrDefault(roleKey) + 1;
            var candidate = new RelationshipResolutionSample(
                edgeKind,
                dispositionName,
                reason,
                edge.SourceId,
                source?.FilePath,
                source?.Properties is not null && source.Properties.TryGetValue("fileRole", out var fileRole)
                    ? fileRole
                    : null,
                source?.LineNumber,
                edge.CallName ?? edge.TargetName,
                edge.ParamCount,
                ReadProperty(edge, "receiverKind"),
                ReadProperty(edge, "receiverTypeHint"));
            AddDeterministicBucketCandidate(candidate);
        }
    }

    public RelationshipResolutionStats Build(int syntheticEdges = 0)
    {
        var stats = new RelationshipResolutionStats(
            _attempted,
            _resolvedLocal,
            _externalOrUnindexed,
            _unresolvedLocal,
            _indeterminate,
            _duplicates,
            syntheticEdges,
            _resolvedLocal - _duplicates,
            _reasons.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            _legacyUnresolved.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            _failureCountsByFileRole.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            SelectDiverseSamples());

        if (!stats.HasValidAccounting)
            throw new InvalidOperationException($"Invalid {edgeKind} relationship accounting.");

        return stats;
    }

    private void AddDeterministicBucketCandidate(RelationshipResolutionSample candidate)
    {
        var existingIndex = _samples.FindIndex(sample =>
            sample.Disposition == candidate.Disposition
            && sample.Reason == candidate.Reason
            && string.Equals(sample.FileRole, candidate.FileRole, StringComparison.Ordinal)
            && string.Equals(sample.ReceiverKind, candidate.ReceiverKind, StringComparison.Ordinal));
        if (existingIndex < 0)
        {
            _samples.Add(candidate);
            return;
        }

        if (CompareSamples(candidate, _samples[existingIndex]) < 0)
            _samples[existingIndex] = candidate;
    }

    private RelationshipResolutionSample[] SelectDiverseSamples() =>
        _samples
            .GroupBy(sample => (sample.Disposition, sample.Reason))
            .OrderBy(group => group.Key.Disposition, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Reason, StringComparer.Ordinal)
            .SelectMany(group => SelectDiverseSamples(group, sampleLimitPerReason))
            .ToArray();

    private static IReadOnlyList<RelationshipResolutionSample> SelectDiverseSamples(
        IEnumerable<RelationshipResolutionSample> candidates,
        int limit)
    {
        var ordered = candidates.Order(Comparer<RelationshipResolutionSample>.Create(CompareSamples)).ToArray();
        var selected = new List<RelationshipResolutionSample>(limit);

        foreach (var candidate in ordered
            .GroupBy(sample => sample.FileRole ?? "Unknown", StringComparer.Ordinal)
            .OrderBy(group => FileRolePriority(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            if (selected.Count == limit)
                return selected;
            selected.Add(candidate);
        }

        foreach (var candidate in ordered
            .Where(candidate => !selected.Contains(candidate))
            .GroupBy(sample => sample.ReceiverKind ?? "Unknown", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            if (selected.Count == limit)
                return selected;
            selected.Add(candidate);
        }

        selected.AddRange(ordered.Where(candidate => !selected.Contains(candidate)).Take(limit - selected.Count));
        return selected;
    }

    private static int CompareSamples(RelationshipResolutionSample left, RelationshipResolutionSample right)
    {
        var fileComparison = StringComparer.Ordinal.Compare(left.FilePath, right.FilePath);
        if (fileComparison != 0)
            return fileComparison;
        var lineComparison = Nullable.Compare(left.LineNumber, right.LineNumber);
        if (lineComparison != 0)
            return lineComparison;
        var sourceComparison = StringComparer.Ordinal.Compare(left.SourceId, right.SourceId);
        return sourceComparison != 0
            ? sourceComparison
            : StringComparer.Ordinal.Compare(left.TargetName, right.TargetName);
    }

    private static int FileRolePriority(string fileRole) =>
        fileRole switch
        {
            "Source" => 0,
            "Test" => 1,
            _ => 2
        };

    private static string BuildEdgeIdentity(IngestEdgeRequest edge) =>
        $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}";

    private static string? ReadProperty(IngestEdgeRequest edge, string key) =>
        edge.Properties is not null && edge.Properties.TryGetValue(key, out var value) ? value : null;

    private static string ToMetadataName(RelationshipResolutionDisposition disposition) =>
        disposition switch
        {
            RelationshipResolutionDisposition.ExternalOrUnindexed => "external_or_unindexed",
            RelationshipResolutionDisposition.UnresolvedLocal => "unresolved_local",
            RelationshipResolutionDisposition.Indeterminate => "indeterminate",
            _ => "resolved_local"
        };
}
