namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>Counts observed candidates, not proof that every language construct was extracted.</summary>
public sealed record CSharpRelationshipSummary(
    int Attempted,
    int ResolvedLocal,
    int ExternalOrUnindexed,
    int UnresolvedLocal,
    int Indeterminate,
    int DuplicateEdges,
    int SyntheticEdges,
    int UniqueResolvedEdges)
{
    public bool HasValidAccounting =>
        Attempted == ResolvedLocal + ExternalOrUnindexed + UnresolvedLocal + Indeterminate
        && UniqueResolvedEdges == ResolvedLocal - DuplicateEdges;

    internal bool HasGaps => !HasValidAccounting || UnresolvedLocal > 0 || Indeterminate > 0;

    internal static CSharpRelationshipSummary From(RelationshipResolutionStats stats) => new(
        stats.Attempted, stats.ResolvedLocal, stats.ExternalOrUnindexed, stats.UnresolvedLocal,
        stats.Indeterminate, stats.DuplicateEdges, stats.SyntheticEdges, stats.UniqueResolvedEdges);
}
