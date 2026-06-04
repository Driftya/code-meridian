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
        CancellationToken cancellationToken = default) =>
        queryService.FindImpactAsync(nodeId, depth, detailLevel, cancellationToken);

}
