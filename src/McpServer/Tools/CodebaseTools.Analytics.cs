using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

// ── Structural analytics tools ────────────────────────────────────────────────
// SRP: this file exposes structural analysis tools only.
// Core query/discovery tools live in CodebaseTools.cs.
// GDS algorithm tools live in CodebaseTools.Gds.cs.

public sealed partial class CodebaseTools
{
    [McpServerTool(Name = "find_hotspots")]
    [Description(
        "Rank code elements by how many other nodes depend on them (fan-in). " +
        "Use at the start of a change session to understand which parts of the codebase carry the most risk. " +
        "High fan-in nodes are the most dangerous to modify — changes ripple furthest.")]
    public Task<string> FindHotspotsAsync(
        [Description("Project name to scope the analysis. Omit to analyse all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindHotspotsAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_connection")]
    [Description(
        "Find the shortest path between two code elements in the graph. " +
        "Use this when the user asks how two classes or methods relate to each other, " +
        "or to trace an execution path between a controller and a data layer.")]
    public Task<string> FindConnectionAsync(
        [Description("ID of the starting node, e.g. 'MyNamespace.OrderController.CreateAsync(CreateOrderRequest,CancellationToken)'")]
        string fromId,
        [Description("ID of the destination node, e.g. 'MyNamespace.PaymentGateway.ChargeAsync(decimal,CancellationToken)'")]
        string toId,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.FindConnectionAsync(fromId, toId, detailLevel, cancellationToken);

    [McpServerTool(Name = "find_unreferenced")]
    [Description(
        "Find methods and classes with no incoming references — dead code candidates. " +
        "Useful before a cleanup task or when trimming a codebase. " +
        "Note: entry points, event handlers, and DI-registered types may appear here even if actively used.")]
    public Task<string> FindUnreferencedAsync(
        [Description("Project name to scope the search. Omit to search all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindUnreferencedAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_cross_project_dependencies")]
    [Description(
        "Find edges that cross project boundaries — where code in one indexed project calls or depends on code in another. " +
        "Use this to understand coupling between services, libraries, and microservices. " +
        "Essential before extracting a module into a separate package or understanding a multi-repo workspace.")]
    public Task<string> FindCrossProjectDependenciesAsync(
        [Description("Scope to a specific project name to see only its cross-project edges. Omit to see all cross-project edges.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindCrossProjectDependenciesAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_coverage_gaps")]
    [Description(
        "Find production classes and methods that no test calls — coverage gap candidates. " +
        "Use this before writing tests to identify the highest-priority untested areas, " +
        "or when doing a code review to flag missing test coverage. " +
        "Detection is heuristic: test classes are identified by namespace or file path containing 'test'.")]
    public Task<string> FindCoverageGapsAsync(
        [Description("Project name to scope the analysis. Omit to check all projects.")]
        string? projectContext = null,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.FindCoverageGapsAsync(projectContext, detailLevel, cancellationToken);

    [McpServerTool(Name = "find_recently_changed")]
    [Description(
        "Find code nodes created or updated within a time window. " +
        "Use this to understand what changed recently — useful for code review context, " +
        "understanding the scope of a PR, or catching regressions. " +
        "Only tracks nodes indexed after timestamps were introduced.")]
    public Task<string> FindRecentlyChangedAsync(
        [Description("Project name to scope the search. Omit to search all projects.")]
        string? projectContext = null,
        [Description("Time window: '24h', '7d', '2h', '30m', etc. Defaults to '24h'.")]
        string window = "24h",
        CancellationToken cancellationToken = default) =>
        queryService.FindRecentlyChangedAsync(projectContext, window, cancellationToken);

    [McpServerTool(Name = "find_large_nodes")]
    [Description(
        "Scan for oversized classes (default >400 lines) and methods (default >40 lines) that violate the " +
        "Single Responsibility Principle. Excludes test files. " +
        "Works for both C# and TypeScript codebases. " +
        "Use before a refactoring session to find the largest SRP violations. " +
        "Requires nodes to have been indexed with a version of the indexer that captures line counts.")]
    public Task<string> FindLargeNodesAsync(
        [Description("Project name to scope the scan. Omit to scan all projects.")]
        string? projectContext = null,
        [Description("Classes with more lines than this are flagged. Default 400.")]
        int classThreshold = 400,
        [Description("Methods with more lines than this are flagged. Default 40.")]
        int methodThreshold = 40,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.FindLargeNodesAsync(projectContext, classThreshold, methodThreshold, detailLevel, cancellationToken);

    [McpServerTool(Name = "get_context_for_editing")]
    [Description(
        "Return the call-graph context for a specific node before editing it: " +
        "direct callers (who will be affected), direct callees (what it depends on), " +
        "interfaces it implements, and its file location and size. " +
        "Call this BEFORE editing any method or class to understand the change surface. " +
        "Provides a compact, context-window-friendly summary for AI coding tools.")]
    public Task<string> GetContextForEditingAsync(
        [Description("ID of the node to get context for, e.g. 'MyNamespace.UserService.SaveAsync(User,CancellationToken)'")]
        string nodeId,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.GetContextForEditingAsync(nodeId, detailLevel, cancellationToken);

    [McpServerTool(Name = "build_minimal_context")]
    [Description(
        "Build a bounded, task-specific context pack for editing or reviewing one code node. " +
        "Use this when you need the smallest useful set of callers, callees, impact, downstream dependencies, " +
        "direct test callers, heuristic test matches, coverage gaps, and likely files before making a change.")]
    public Task<string> BuildMinimalContextAsync(
        [Description("Target node ID to build context for, preferably the exact ID returned by query_codebase.")]
        string target,
        [Description("Optional plain-language goal for the change, used to label the context pack.")]
        string? goal = null,
        [Description("Maximum desired token budget for the returned context. Default 3000.")]
        int maxTokens = 3000,
        [Description("Whether to include direct test callers, heuristic test matches, and relevant coverage gaps. Default true.")]
        bool includeTests = true,
        [Description("Whether to include external-concept hints when graph data contains them. Default true.")]
        bool includeExternalConcepts = true,
        [Description("Whether to request source snippets. Source extraction is currently reported as guidance. Default false.")]
        bool includeSourceSnippets = false,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.BuildMinimalContextAsync(
            target,
            goal,
            maxTokens,
            includeTests,
            includeExternalConcepts,
            includeSourceSnippets,
            detailLevel,
            cancellationToken);

    [McpServerTool(Name = "find_god_classes")]
    [Description(
        "Find classes that are both large (SRP violation) and heavily depended upon (high fan-in). " +
        "These are the highest-risk refactoring targets in the codebase. " +
        "Works for both C# and TypeScript codebases. " +
        "Ranked by a combined risk score of size × coupling. " +
        "Requires line count data — re-index if results are empty.")]
    public Task<string> FindGodClassesAsync(
        [Description("Project name to scope the scan. Omit to scan all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindGodClassesAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_downstream")]
    [Description(
        "Traverse the call graph FORWARD to find everything this node transitively calls or depends on. " +
        "Complements find_impact (backward). Together they give the full change surface around any node. " +
        "Use this when you want to know 'what would break if the implementation of X changes internally'.")]
    public Task<string> FindDownstreamAsync(
        [Description("ID of the node to analyse, e.g. 'MyNamespace.UserService.SaveAsync(User,CancellationToken)'")]
        string nodeId,
        [Description("How many hops to traverse. Default 5, max practical is 8.")]
        int depth = 5,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) =>
        queryService.FindDownstreamAsync(nodeId, depth, detailLevel, cancellationToken);

    [McpServerTool(Name = "find_cycles")]
    [Description(
        "Detect namespace-level circular dependencies: pairs of namespaces where A depends on B AND B depends on A. " +
        "Circular dependencies are a leading cause of build-order failures and tight coupling. " +
        "Call this during architecture review or when planning to extract a module.")]
    public Task<string> FindCyclesAsync(
        [Description("Project name to scope the analysis. Omit to check all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindCyclesAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_architecture_violations")]
    [Description(
        "Check for Clean Architecture layer violations: Core depending on Application/Infrastructure/McpServer, " +
        "or Application depending on Infrastructure/McpServer. " +
        "Run this on every PR to enforce architectural boundaries automatically. " +
        "Uses namespace patterns to classify layers.")]
    public Task<string> FindArchitectureViolationsAsync(
        [Description("Project name to scope the check. Omit to check all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindArchitectureViolationsAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_high_churn")]
    [Description(
        "Find code nodes that have been re-indexed the most times — a proxy for files that change frequently. " +
        "High churn combined with high fan-in (from find_hotspots) identifies the highest technical-debt risk areas. " +
        "Based on the changeCount property incremented by the indexer on every run.")]
    public Task<string> FindHighChurnAsync(
        [Description("Project name to scope the analysis. Omit to analyse all projects.")]
        string? projectContext = null,
        [Description("Minimum number of re-indexes to be considered high churn. Default 3.")]
        int threshold = 3,
        CancellationToken cancellationToken = default) =>
        queryService.FindHighChurnAsync(projectContext, threshold, cancellationToken);
}
