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
    IReadOnlyList<RelationshipResolutionSample> Samples)
{
    public bool HasValidAccounting =>
        Attempted == ResolvedLocal + ExternalOrUnindexed + UnresolvedLocal + Indeterminate;
}

internal sealed class RelationshipResolutionCollector(string edgeKind, int sampleLimitPerReason = 3)
{
    private readonly Dictionary<string, int> _reasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _legacyUnresolved = new(StringComparer.Ordinal);
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

        if (disposition is RelationshipResolutionDisposition.UnresolvedLocal or RelationshipResolutionDisposition.Indeterminate
            && _samples.Count(sample => sample.Disposition == dispositionName && sample.Reason == reason) < sampleLimitPerReason)
        {
            _samples.Add(new RelationshipResolutionSample(
                edgeKind,
                dispositionName,
                reason,
                edge.SourceId,
                source?.FilePath,
                source?.LineNumber,
                edge.CallName ?? edge.TargetName,
                edge.ParamCount,
                ReadProperty(edge, "receiverKind"),
                ReadProperty(edge, "receiverTypeHint")));
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
            _samples
                .OrderBy(sample => sample.Disposition, StringComparer.Ordinal)
                .ThenBy(sample => sample.Reason, StringComparer.Ordinal)
                .ThenBy(sample => sample.SourceId, StringComparer.Ordinal)
                .ThenBy(sample => sample.TargetName, StringComparer.Ordinal)
                .ToArray());

        if (!stats.HasValidAccounting)
            throw new InvalidOperationException($"Invalid {edgeKind} relationship accounting.");

        return stats;
    }

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
