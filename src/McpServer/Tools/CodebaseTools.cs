using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

/// <summary>
/// Read-only tools Copilot uses to understand your codebase.
/// Copilot decides autonomously when to call these based on your chat message.
/// </summary>
[McpServerToolType]
public sealed partial class CodebaseTools(ICodebaseQueryService queryService)
{
    [McpServerTool(Name = "query_codebase")]
    [Description(
        "Query the code knowledge graph for structural information about classes, methods, " +
        "interfaces, call graphs, and dependencies. " +
        "Use this before suggesting changes to understand the existing architecture. " +
        "Examples: 'who calls UserService', 'classes implementing IRepository', " +
        "'dependencies of OrderController', 'where is authentication handled'.")]
    public Task<string> QueryCodebaseAsync(
        [Description("Natural language query about code structure, e.g. 'callers of SaveAsync' or 'classes in the payments module'")]
        string query,
        [Description("Optional project name to narrow the search (e.g. 'MyApi', 'AuthService'). Omit to search all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.QueryStructureAsync(query, projectContext, cancellationToken);

    [McpServerTool(Name = "get_architectural_overview")]
    [Description(
        "Get a high-level overview of a project's structure: namespaces, class count, key interfaces, and top-level modules. " +
        "Use this at the start of a session to orient yourself before diving into details. " +
        "Helps identify patterns, boundaries, and potential impact areas.")]
    public Task<string> GetArchitecturalOverviewAsync(
        [Description("Project name to inspect (e.g. 'MyApi'). Omit to get an overview across all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetOverviewAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "search_documentation")]
    [Description(
        "Full-text search over ingested documentation, ADRs, README files, and code comments. " +
        "Use this when the user asks about decisions, patterns, or concepts rather than specific code elements.")]
    public Task<string> SearchDocumentationAsync(
        [Description("Search query, e.g. 'authentication strategy', 'why was Redis chosen', 'retry policy'")]
        string query,
        [Description("Optional project name to scope the search.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.SearchDocumentationAsync(query, projectContext, cancellationToken);

    [McpServerTool(Name = "find_impact")]
    [Description(
        "Traverse the call graph backwards to find everything that would be affected by changing a method or class. " +
        "ALWAYS call this before suggesting a refactor or edit to understand blast radius. " +
        "Identifies callers, transitive dependents, and cross-namespace effects. " +
        "Use node IDs in the form 'Namespace.ClassName.MethodName' or the id returned by query_codebase.")]
    public Task<string> FindImpactAsync(
        [Description("ID of the node to analyse, e.g. 'MyNamespace.UserService.SaveAsync(User,CancellationToken)'")]
        string nodeId,
        [Description("How many hops to traverse. Default 5, max practical is 8.")]
        int depth = 5,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        [Description("Whether to separate proven callers, heuristic callers, and unknown-risk nodes using path and freshness signals. Default false.")]
        bool includeConfidence = false,
        CancellationToken cancellationToken = default) =>
        queryService.FindImpactAsync(nodeId, depth, detailLevel, includeConfidence, cancellationToken);

    [McpServerTool(Name = "find_diagnostics")]
    [Description(
        "Find indexed compiler, analyzer, TypeScript, or lint diagnostics for a project. " +
        "Use this to understand current build/type/lint failures before editing.")]
    public Task<string> FindDiagnosticsAsync(
        [Description("Optional project name to scope diagnostics.")]
        string? projectContext = null,
        [Description("Optional severity filter, e.g. 'error', 'warning', or 'info'.")]
        string? severity = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindDiagnosticsAsync(projectContext, severity, cancellationToken);

    [McpServerTool(Name = "find_diagnostics_for_node")]
    [Description(
        "Find indexed diagnostics in the same file as a code node, ordered by proximity to the node line. " +
        "Use this before editing a method or class to see nearby compiler/type/lint problems.")]
    public Task<string> FindDiagnosticsForNodeAsync(
        [Description("ID of the node to inspect for nearby diagnostics.")]
        string nodeId,
        CancellationToken cancellationToken = default) =>
        queryService.FindDiagnosticsForNodeAsync(nodeId, cancellationToken);

    [McpServerTool(Name = "find_stale_knowledge")]
    [Description(
        "Detect potentially stale knowledge in ingested docs, external concept links, and orphaned graph references. " +
        "Use this when persistent memory may be out of date after code renames, reindexing, or documentation drift.")]
    public Task<string> FindStaleKnowledgeAsync(
        [Description("Optional project name to scope the analysis.")]
        string? projectContext = null,
        [Description("Maximum number of findings to include in the report.")]
        int limit = 25,
        CancellationToken cancellationToken = default) =>
        queryService.FindStaleKnowledgeAsync(projectContext, limit, cancellationToken);

    [McpServerTool(Name = "find_implementation_surface")]
    [Description(
        "Find the most likely files, classes, and methods to edit for a requested feature or fix. " +
        "Use this before implementation when the user describes a goal rather than a precise node ID.")]
    public Task<string> FindImplementationSurfaceAsync(
        [Description("Feature or fix goal, e.g. 'add stale knowledge query'.")]
        string goal,
        [Description("Optional comma-separated concepts that should influence targeting, e.g. 'knowledge,document,stale'.")]
        string? conceptsCsv = null,
        [Description("Optional project name to scope the search.")]
        string? projectContext = null,
        [Description("Maximum number of implementation targets to return.")]
        int limit = 12,
        CancellationToken cancellationToken = default) =>
        queryService.FindImplementationSurfaceAsync(goal, conceptsCsv, projectContext, limit, cancellationToken);

    [McpServerTool(Name = "plan_edit_route")]
    [Description(
        "Plan an ordered edit route for a feature or refactor goal. " +
        "Use this before implementation when the user wants an itinerary across contracts, application/domain behavior, " +
        "infrastructure implementations, DI/API entry points, and tests instead of a flat file list.")]
    public Task<string> PlanEditRouteAsync(
        [Description("Feature or refactor goal, e.g. 'replace repository pattern in payments'.")]
        string goal,
        [Description("Optional comma-separated concepts that should influence route planning, e.g. 'repository,payments,DI,tests'.")]
        string? conceptsCsv = null,
        [Description("Optional project name to scope the route.")]
        string? projectContext = null,
        [Description("Maximum number of initial implementation candidates to inspect.")]
        int limit = 8,
        CancellationToken cancellationToken = default) =>
        queryService.PlanEditRouteAsync(goal, conceptsCsv, projectContext, limit, cancellationToken);

    [McpServerTool(Name = "resolve_exact_symbol")]
    [Description(
        "Resolve a method, class, interface, or file hint to canonical CodeMeridian node IDs. " +
        "Use this when query_codebase or find_implementation_surface found a likely file but you need the exact node ID before editing.")]
    public Task<string> ResolveExactSymbolAsync(
        [Description("Symbol name or partial name, e.g. 'BuildMinimalContextAsync' or 'CodebaseQueryService'.")]
        string symbol,
        [Description("Optional indexed file path to narrow the lookup, e.g. 'src/Application/Services/CodebaseQueryService.Analytics.cs'.")]
        string? filePath = null,
        [Description("Optional source line hint. Results nearest to this line are ranked first.")]
        int? line = null,
        [Description("Optional project name to scope the search.")]
        string? projectContext = null,
        [Description("Maximum number of candidate symbols to return.")]
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        queryService.ResolveExactSymbolAsync(symbol, filePath, line, projectContext, limit, cancellationToken);

    [McpServerTool(Name = "check_graph_freshness")]
    [Description(
        "Report freshness and confidence for graph nodes using indexer-supplied file, line, and timestamp metadata. " +
        "Use this when CodeMeridian results may be stale or only partially trusted. Source files are not read from the MCP server.")]
    public Task<string> CheckGraphFreshnessAsync(
        [Description("Optional search query to inspect matching nodes. Omit to sample the project graph.")]
        string? query = null,
        [Description("Optional project name to scope freshness checks.")]
        string? projectContext = null,
        [Description("Maximum number of nodes to inspect.")]
        int limit = 25,
        CancellationToken cancellationToken = default) =>
        queryService.CheckGraphFreshnessAsync(query, projectContext, limit, cancellationToken);

    [McpServerTool(Name = "find_graph_drift")]
    [Description(
        "Detect graph drift before implementation by checking missing indexed file metadata, incomplete line metadata, and missing update timestamps. " +
        "Use this before relying on exact graph targets after renames, broad refactors, or indexer changes.")]
    public Task<string> FindGraphDriftAsync(
        [Description("Optional project name to scope drift checks.")]
        string? projectContext = null,
        [Description("Maximum number of drift findings to include per section.")]
        int limit = 25,
        CancellationToken cancellationToken = default) =>
        queryService.FindGraphDriftAsync(projectContext, limit, cancellationToken);

}
