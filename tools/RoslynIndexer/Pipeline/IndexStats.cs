namespace CodeMeridian.RoslynIndexer.Pipeline;

public sealed record IndexStats(
    int Nodes,
    int Edges,
    int ScannedFiles,
    int IngestedFiles,
    int AttemptedCallEdges,
    int ResolvedCallEdges,
    int AttemptedReferenceEdges,
    int ResolvedReferenceEdges,
    IReadOnlyDictionary<string, int> UnresolvedEdgesByReason,
    RelationshipResolutionStats CallResolution,
    RelationshipResolutionStats ReferenceResolution,
    string Mode,
    bool UsedFullResolutionCatalog,
    IReadOnlyDictionary<string, int> EdgeEvidenceCounts,
    IReadOnlyDictionary<string, int> EdgeEvidenceGroups);
public sealed record DocumentStats(int Documents);
