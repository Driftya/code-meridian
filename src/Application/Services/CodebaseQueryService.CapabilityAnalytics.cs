namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<string> FindHotspotsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_hotspots", projectContext, null,
            () => FindHotspotsCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindUnreferencedAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_unreferenced", projectContext, null,
            () => FindUnreferencedCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindCoverageGapsAsync(
        string? projectContext = null,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_coverage_gaps", projectContext, null,
            () => FindCoverageGapsCoreAsync(projectContext, detailLevel, cancellationToken), cancellationToken);

    public Task<string> FindRecentlyChangedAsync(
        string? projectContext = null,
        string window = "24h",
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_recently_changed", projectContext, null,
            () => FindRecentlyChangedCoreAsync(projectContext, window, cancellationToken), cancellationToken);

    public Task<string> FindLargeNodesAsync(
        string? projectContext = null,
        int classThreshold = 400,
        int methodThreshold = 40,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_large_nodes", projectContext, null,
            () => FindLargeNodesCoreAsync(projectContext, classThreshold, methodThreshold, detailLevel, cancellationToken), cancellationToken);

    public Task<string> GetContextForEditingAsync(
        string nodeId,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("get_context_for_editing", null, nodeId,
            () => GetContextForEditingCoreAsync(nodeId, detailLevel, cancellationToken), cancellationToken);

    public Task<string> FindGodClassesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_god_classes", projectContext, null,
            () => FindGodClassesCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindStaleKnowledgeAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_stale_knowledge", projectContext, null,
            () => FindStaleKnowledgeCoreAsync(projectContext, limit, cancellationToken), cancellationToken);

    public Task<string> FindDownstreamAsync(
        string nodeId,
        int depth = 5,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_downstream", null, nodeId,
            () => FindDownstreamCoreAsync(nodeId, depth, detailLevel, cancellationToken), cancellationToken);

    public Task<string> FindCyclesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_cycles", projectContext, null,
            () => FindCyclesCoreAsync(projectContext, cancellationToken), cancellationToken);

    public Task<string> FindArchitectureErosionTimelineAsync(
        string? projectContext = null,
        int days = 30,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("architecture_drift_history", projectContext, null,
            () => FindArchitectureErosionTimelineCoreAsync(projectContext, days, cancellationToken), cancellationToken);

    public async Task<string> FindArchitectureViolationsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        Projects.ProjectProfileResult? initial = null;
        if (projectProfiles is not null && !string.IsNullOrWhiteSpace(projectContext))
        {
            try { initial = await projectProfiles.GetAsync(projectContext, cancellationToken: cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return "Architecture report unavailable: capability evidence could not be read.";
            }
        }
        var namespaces = await ExecuteCapabilityQueryAsync("find_architecture_violations", projectContext, null,
            () => FindArchitectureViolationsCoreAsync(projectContext, cancellationToken), cancellationToken);
        RepositoryConstraints.RepositoryConstraintQueryResult? constraints;
        try
        {
            constraints = projectProfiles is null || string.IsNullOrWhiteSpace(projectContext) ? null
                : await projectProfiles.FindRepositoryConstraintsAsync(projectContext, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return namespaces + "\n\nRepository constraints unavailable: evidence could not be read.";
        }
        if (constraints is null) return namespaces;
        Projects.ProjectProfileResult current;
        try { current = await projectProfiles!.GetAsync(projectContext!, cancellationToken: cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Architecture report unavailable: evidence revalidation failed; all findings were discarded.";
        }
        if (current.Status != "available" || current.Generation != initial?.Generation || constraints.Generation != initial?.Generation
            || current.Profile?.SourceFingerprint != initial?.Profile?.SourceFingerprint
            || current.Profile?.ConfigurationFingerprint != initial?.Profile?.ConfigurationFingerprint)
            return "Architecture report unavailable: evidence changed during evaluation; all findings were discarded.";
        return namespaces + "\n\n" + constraints.ToMarkdown();
    }
}
