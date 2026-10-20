namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<string> FindFrontendCascadeConflictsAsync(
        string? projectContext = null,
        string? filter = null,
        bool excludeTests = true,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_frontend_cascade_conflicts", projectContext, null,
            () => FindFrontendCascadeConflictsCoreAsync(projectContext, filter, excludeTests, cancellationToken), cancellationToken);

    public Task<string> GetPageRankAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("get_pagerank", projectContext, null,
            () => GetPageRankCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> GetBetweennessAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("get_betweenness", projectContext, null,
            () => GetBetweennessCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindNaturalModulesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_natural_modules", projectContext, null,
            () => FindNaturalModulesCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> SuggestExtractionsAsync(
        string? projectContext = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("suggest_extractions", projectContext, null,
            () => SuggestExtractionsCoreAsync(projectContext, limit, cancellationToken), cancellationToken);

    public Task<string> FindSimilarToNodeAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_similar_nodes", projectContext, nodeId,
            () => FindSimilarToNodeCoreAsync(nodeId, projectContext, cancellationToken), cancellationToken);

    public Task<string> FindHybridSearchAsync(
        string query,
        string? nearNodeId = null,
        int maxHops = 3,
        string? projectContext = null,
        bool excludeTests = true,
        int limit = 10,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("hybrid_search", projectContext, nearNodeId,
            () => FindHybridSearchCoreAsync(query, nearNodeId, maxHops, projectContext, excludeTests, limit, cancellationToken), cancellationToken);

    public Task<string> FindDuplicateCandidatesAsync(
        string? projectContext = null,
        string? namespaceFilter = null,
        string? nodeType = null,
        int minLineCount = 5,
        double minSimilarity = 0.88,
        bool excludeTests = true,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_duplicate_candidates", projectContext, null,
            () => FindDuplicateCandidatesCoreAsync(projectContext, namespaceFilter, nodeType, minLineCount, minSimilarity, excludeTests, cancellationToken), cancellationToken);

    public Task<string> FindImplementationPatternsAsync(
        string query,
        string? projectContext = null,
        bool excludeTests = true,
        int limit = 5,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_implementation_patterns", projectContext, null,
            () => FindImplementationPatternsCoreAsync(query, projectContext, excludeTests, limit, cancellationToken), cancellationToken);

    public Task<string> ReplaceSurfaceAsync(
        string fromDependency,
        string toDependency,
        string? projectContext = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("replace_surface", projectContext, null,
            () => ReplaceSurfaceCoreAsync(fromDependency, toDependency, projectContext, limit, cancellationToken), cancellationToken);

    public Task<string> SuggestResponsibilitySlicesAsync(
        string target,
        string? projectContext = null,
        int maxSlices = 6,
        bool includeNamespacePlan = true,
        bool includeTestPlan = true,
        bool includeMigrationSteps = true,
        bool includeSourceSnippets = false,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("suggest_responsibility_slices", projectContext, null,
            () => SuggestResponsibilitySlicesCoreAsync(target, projectContext, maxSlices, includeNamespacePlan, includeTestPlan, includeMigrationSteps, includeSourceSnippets, cancellationToken), cancellationToken);

    public Task<string> FindBridgesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_bridges", projectContext, null,
            () => FindBridgesCoreAsync(projectContext, cancellationToken), cancellationToken);
}
