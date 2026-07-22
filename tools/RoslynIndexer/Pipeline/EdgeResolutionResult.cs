namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record EdgeResolutionResult(
    List<IngestEdgeRequest> Edges,
    int Attempted,
    int Resolved,
    IReadOnlyDictionary<string, int> UnresolvedByReason);

internal sealed class EdgeResolutionDiagnostics
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public void Add(string reason)
    {
        _counts[reason] = _counts.GetValueOrDefault(reason) + 1;
    }

    public IReadOnlyDictionary<string, int> Snapshot() => _counts;
}
