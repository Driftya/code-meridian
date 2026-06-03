namespace CodeMeridian.Application.Services;

public interface ICodebaseQueryService
{
    Task<string> QueryStructureAsync(string query, string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> GetOverviewAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> SearchDocumentationAsync(string query, string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindImpactAsync(string nodeId, int depth = 5, CancellationToken cancellationToken = default);
    Task<string> FindHotspotsAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindConnectionAsync(string fromId, string toId, CancellationToken cancellationToken = default);
    Task<string> FindUnreferencedAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindCrossProjectDependenciesAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindCoverageGapsAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindRecentlyChangedAsync(string? projectContext = null, string window = "24h", CancellationToken cancellationToken = default);
    Task<string> FindLargeNodesAsync(string? projectContext = null, int classThreshold = 400, int methodThreshold = 40, CancellationToken cancellationToken = default);
    Task<string> GetContextForEditingAsync(string nodeId, CancellationToken cancellationToken = default);
    Task<string> FindGodClassesAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    // ── New capabilities ──────────────────────────────────────────────────────
    Task<string> FindDownstreamAsync(string nodeId, int depth = 5, CancellationToken cancellationToken = default);
    Task<string> FindCyclesAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindArchitectureViolationsAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindHighChurnAsync(string? projectContext = null, int threshold = 3, CancellationToken cancellationToken = default);
    Task<string> GetPageRankAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> GetBetweennessAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindNaturalModulesAsync(string? projectContext = null, CancellationToken cancellationToken = default);
    Task<string> FindSimilarToNodeAsync(string nodeId, string? projectContext = null, CancellationToken cancellationToken = default);
}
