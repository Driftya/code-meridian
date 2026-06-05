namespace CodeMeridian.Core.CodeGraph;

public interface ICodeGraphRepository
{
    Task<IReadOnlyList<CodeNode>> QueryNodesAsync(CodeGraphQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeEdge>> QueryEdgesAsync(string nodeId, int depth = 1, CancellationToken cancellationToken = default);
    Task<string> GetSubgraphSummaryAsync(string nodeId, CancellationToken cancellationToken = default);
    Task UpsertNodeAsync(CodeNode node, CancellationToken cancellationToken = default);
    Task UpsertEdgeAsync(CodeEdge edge, CancellationToken cancellationToken = default);
    Task DeleteProjectAsync(string projectContext, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string projectContext, string filePath, CancellationToken cancellationToken = default);
    Task DeleteDiagnosticsAsync(string projectContext, CancellationToken cancellationToken = default);
    Task DeleteAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns nodes that transitively depend on <paramref name="nodeId"/> up to <paramref name="depth"/> hops.</summary>
    Task<IReadOnlyList<(CodeNode Node, int Distance)>> FindImpactAsync(string nodeId, int depth = 5, CancellationToken cancellationToken = default);

    /// <summary>Returns nodes with the highest incoming-edge count (fan-in) in a project.</summary>
    Task<IReadOnlyList<(CodeNode Node, int FanIn)>> FindHotspotsAsync(string? projectContext, int limit = 15, CancellationToken cancellationToken = default);

    /// <summary>Returns the shortest path between two nodes, or empty if no path exists.</summary>
    Task<IReadOnlyList<(CodeNode Node, string? ViaRelationship)>> FindConnectionAsync(string fromId, string toId, CancellationToken cancellationToken = default);

    /// <summary>Returns methods and classes with no incoming Calls/Uses/Contains edges (potential dead code).</summary>
    Task<IReadOnlyList<CodeNode>> FindUnreferencedAsync(string? projectContext, CancellationToken cancellationToken = default);

    /// <summary>Returns edges that cross project boundaries — nodes in one project depending on nodes in another.</summary>
    Task<IReadOnlyList<(CodeNode Source, CodeNode Target, string RelationshipType)>> FindCrossProjectDependenciesAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>Returns production classes/methods that no test node has a Calls edge to.</summary>
    Task<IReadOnlyList<CodeNode>> FindCoverageGapsAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>Returns tests related to a node via direct calls or heuristic proximity.</summary>
    Task<IReadOnlyList<(CodeNode Node, string MatchType)>> FindRelatedTestsAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns nodes created or updated within the given window.</summary>
    Task<IReadOnlyList<(CodeNode Node, DateTimeOffset ChangedAt, string ChangeType)>> FindRecentlyChangedAsync(string? projectContext, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>Returns classes and methods whose line count exceeds the given thresholds, excluding test files.</summary>
    Task<IReadOnlyList<CodeNode>> FindLargeNodesAsync(string? projectContext = null, int classThreshold = 400, int methodThreshold = 40, CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated call-graph context for a node — callers, callees, and interfaces — optimised for AI context windows.</summary>
    Task<EditingContext> GetContextForEditingAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>Returns classes that are both large and heavily depended upon — the highest SRP-violation risk in the codebase.</summary>
    Task<IReadOnlyList<(CodeNode Node, int LineCount, int FanIn)>> FindGodClassesAsync(string? projectContext = null, int lineThreshold = 300, int fanInThreshold = 3, CancellationToken cancellationToken = default);

    /// <summary>Forward blast radius — everything this node transitively calls/depends on.</summary>
    Task<IReadOnlyList<(CodeNode Node, int Distance)>> FindDownstreamAsync(string nodeId, int depth = 5, CancellationToken cancellationToken = default);

    /// <summary>Detect namespace-level circular dependencies (A calls B and B calls A).</summary>
    Task<IReadOnlyList<(string FromNamespace, string ToNamespace)>> FindCyclesAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>Find Clean Architecture layer violations (e.g. Core depending on Infrastructure).</summary>
    Task<IReadOnlyList<(CodeNode Source, CodeNode Target, string Violation)>> FindArchitectureViolationsAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>Return nodes with the highest re-index count — frequently changed files.</summary>
    Task<IReadOnlyList<(CodeNode Node, int ChangeCount)>> FindHighChurnAsync(string? projectContext = null, int threshold = 3, CancellationToken cancellationToken = default);

    /// <summary>GDS PageRank — architecturally most influential nodes by transitive call-graph weight.</summary>
    Task<IReadOnlyList<(CodeNode Node, double Score)>> GetPageRankAsync(string? projectContext = null, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>GDS Betweenness Centrality — bridge nodes that sit between subsystems (highest connective tissue risk).</summary>
    Task<IReadOnlyList<(CodeNode Node, double Score)>> GetBetweennessAsync(string? projectContext = null, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>GDS Louvain community detection — natural module boundaries the code has organically evolved into.</summary>
    Task<IReadOnlyList<(CodeNode Node, long Community)>> FindNaturalModulesAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>Native vector similarity — find nodes semantically similar to the given node using stored embeddings.</summary>
    Task<IReadOnlyList<(CodeNode Node, double Score)>> FindSimilarToNodeAsync(string nodeId, string? projectContext = null, int topK = 10, CancellationToken cancellationToken = default);

    /// <summary>Find semantically similar method/class pairs that are candidates for duplicate-code review.</summary>
    Task<IReadOnlyList<DuplicateCandidate>> FindDuplicateCandidatesAsync(
        string? projectContext = null,
        string? namespaceFilter = null,
        CodeNodeType? nodeType = null,
        int minLineCount = 5,
        double minSimilarity = 0.88,
        bool excludeTests = true,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Returns indexed diagnostics for a project, optionally filtered by severity.</summary>
    Task<IReadOnlyList<CodeNode>> FindDiagnosticsAsync(string? projectContext = null, string? severity = null, CancellationToken cancellationToken = default);

    /// <summary>Returns diagnostics attached to the same file as the target node.</summary>
    Task<IReadOnlyList<CodeNode>> FindDiagnosticsForNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent code-node update timestamp for a project, or null if no nodes exist.</summary>
    Task<DateTimeOffset?> GetMostRecentCodeUpdateAsync(string? projectContext = null, CancellationToken cancellationToken = default);
}
