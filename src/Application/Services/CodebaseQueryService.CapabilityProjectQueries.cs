namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<string> FindHighChurnAsync(
        string? projectContext = null,
        int threshold = 3,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_high_churn", projectContext, null,
            () => FindHighChurnCoreAsync(projectContext, threshold, cancellationToken), cancellationToken);

    public Task<string> FindSmellPathsAsync(
        string? projectContext = null,
        int maxDepth = 4,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_smell_paths", projectContext, null,
            () => FindSmellPathsCoreAsync(projectContext, maxDepth, cancellationToken), cancellationToken);

    public Task<string> GetArchitectureWeatherReportAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("get_architecture_weather_report", projectContext, null,
            () => GetArchitectureWeatherReportCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> AnalyzeChangedSubgraphAsync(
        IReadOnlyCollection<string> changedFiles,
        string? projectContext = null,
        int impactDepth = 2,
        int limit = 10,
        bool includeDocs = true,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("analyze_changed_subgraph", projectContext, null,
            () => AnalyzeChangedSubgraphCoreAsync(changedFiles, projectContext, impactDepth, limit, includeDocs, cancellationToken), cancellationToken,
            new(IncludeDocs: includeDocs));

    public Task<string> FindConfigDefinitionsAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_config_definitions", projectContext, null,
            () => FindConfigDefinitionsCoreAsync(canonicalKey, projectContext, cancellationToken), cancellationToken);

    public Task<string> FindConfigUsageAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_config_usage", projectContext, null,
            () => FindConfigUsageCoreAsync(canonicalKey, projectContext, cancellationToken), cancellationToken);

    public Task<string> QueryStructureAsync(
        string query,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("query_codebase", projectContext, null,
            () => QueryStructureCoreAsync(query, projectContext, cancellationToken), cancellationToken);

    public Task<string> GetOverviewAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("get_architectural_overview", projectContext, null,
            () => GetOverviewCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindDiagnosticsAsync(
        string? projectContext = null,
        string? severity = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_diagnostics", projectContext, null,
            () => FindDiagnosticsCoreAsync(projectContext, severity, cancellationToken), cancellationToken);

    public Task<string> FindDiagnosticsForNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_diagnostics_for_node", null, nodeId,
            () => FindDiagnosticsForNodeCoreAsync(nodeId, cancellationToken), cancellationToken);

    public Task<string> TraceEndpointAsync(
        string route,
        string? projectContext = null,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("trace_endpoint", projectContext, null,
            () => TraceEndpointCoreAsync(route, projectContext, detailLevel, cancellationToken), cancellationToken);

    public Task<string> AnalyzeFeatureImplementationPathAsync(
        string feature,
        string? projectContext = null,
        bool includeTests = true,
        bool includeDocs = true,
        bool includeRisk = true,
        int limit = 12,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("analyze_feature_implementation_path", projectContext, null,
            () => AnalyzeFeatureImplementationPathCoreAsync(feature, projectContext, includeTests, includeDocs, includeRisk, limit, cancellationToken), cancellationToken,
            new(IncludeTests: includeTests, IncludeDocs: includeDocs));
}
