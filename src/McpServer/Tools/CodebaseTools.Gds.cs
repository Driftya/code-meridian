using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

// ── GDS (Graph Data Science) algorithm tools ──────────────────────────────────
// SRP: this file exposes Neo4j GDS algorithm tools only.
// Core query/discovery tools live in CodebaseTools.cs.
// Structural analytics tools live in CodebaseTools.Analytics.cs.

public sealed partial class CodebaseTools
{
    [McpServerTool(Name = "get_pagerank")]
    [Description(
        "Run GDS PageRank on the call graph to find the most architecturally influential nodes. " +
        "Unlike fan-in (find_hotspots), PageRank accounts for the importance of callers themselves — " +
        "a node called by a highly connected node ranks higher. " +
        "Requires Neo4j Graph Data Science plugin (included in docker-compose by default).")]
    public Task<string> GetPageRankAsync(
        [Description("Project name to scope the results. Omit to rank across all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetPageRankAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "get_betweenness")]
    [Description(
        "Run GDS Betweenness Centrality to find bridge nodes — code that sits between subsystems. " +
        "High betweenness nodes are the connective tissue of your architecture. " +
        "Removing or changing them disconnects large parts of the system. " +
        "Requires Neo4j Graph Data Science plugin.")]
    public Task<string> GetBetweennessAsync(
        [Description("Project name to scope the results. Omit to analyse all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetBetweennessAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_bridges")]
    [Description(
        "Find risky core nodes by combining GDS betweenness centrality, PageRank, articulation-point detection, bridge-edge detection, and direct graph context. " +
        "Reports why a node is structurally risky, which layers it connects, and the next CodeMeridian tool to use before refactoring it. Requires Neo4j Graph Data Science plugin.")]
    public Task<string> FindBridgesAsync(
        [Description("Project name to scope the results. Omit to analyse all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindBridgesAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "find_natural_modules")]
    [Description(
        "Run GDS Louvain community detection to discover the organic module boundaries that the codebase has evolved into. " +
        "Communities are clusters of tightly interconnected code that call each other more than they call outside code. " +
        "Compare communities with your folder/namespace structure to spot hidden coupling or misplaced code. " +
        "Requires Neo4j Graph Data Science plugin.")]
    public Task<string> FindNaturalModulesAsync(
        [Description("Project name to scope the detection. Omit to detect communities across all projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindNaturalModulesAsync(projectContext, cancellationToken);

    [McpServerTool(Name = "suggest_extractions")]
    [Description(
        "Suggest refactor extraction candidates by ranking tightly connected natural modules that look safe to peel out. " +
        "Combines Louvain communities with hotspot, large-class, nearby-test, and coverage-gap signals so the result is explainable instead of speculative. " +
        "Requires Neo4j Graph Data Science plugin for community detection.")]
    public Task<string> SuggestExtractionsAsync(
        [Description("Project name to scope candidate extraction clusters. Omit to analyse all projects.")]
        string? projectContext = null,
        [Description("Maximum number of extraction candidates to return. Default 8.")]
        int limit = 8,
        CancellationToken cancellationToken = default) =>
        queryService.SuggestExtractionsAsync(projectContext, limit, cancellationToken);

    [McpServerTool(Name = "suggest_responsibility_slices")]
    [Description(
        "Suggest responsibility-based extraction slices for a large class or service. " +
        "Clusters indexed methods with graph evidence from shared dependencies, workflow callers, tests, docs, and existing namespace/folder patterns. " +
        "Returns folder, namespace, service, test, and migration recommendations without editing files.")]
    public Task<string> SuggestResponsibilitySlicesAsync(
        [Description("Class or service name to analyze, e.g. 'CodebaseQueryService'.")]
        string target,
        [Description("Project name to scope the analysis. Omit to analyse all projects.")]
        string? projectContext = null,
        [Description("Maximum number of responsibility slices to return. Default 6.")]
        int maxSlices = 6,
        [Description("Include recommended folder and namespace plan. Default true.")]
        bool includeNamespacePlan = true,
        [Description("Include related-test and missing-test notes. Default true.")]
        bool includeTestPlan = true,
        [Description("Include facade/direct/defer migration steps. Default true.")]
        bool includeMigrationSteps = true,
        [Description("Include source snippets when supported. Default false.")]
        bool includeSourceSnippets = false,
        CancellationToken cancellationToken = default) =>
        queryService.SuggestResponsibilitySlicesAsync(
            target,
            projectContext,
            maxSlices,
            includeNamespacePlan,
            includeTestPlan,
            includeMigrationSteps,
            includeSourceSnippets,
            cancellationToken);

    [McpServerTool(Name = "find_similar_nodes")]
    [Description(
        "Find code nodes semantically similar to the given node using native Neo4j vector embeddings. " +
        "Unlike structural queries, this finds conceptually related code regardless of call-graph proximity — " +
        "useful for finding duplicate logic, related implementations, or candidates for extraction. " +
        "Requires embeddings to be stored on nodes via ingest_code_node (embeddingCsv parameter).")]
    public Task<string> FindSimilarToNodeAsync(
        [Description("ID of the reference node, e.g. 'MyNamespace.UserService.SaveAsync'")]
        string nodeId,
        [Description("Optional project name to narrow the similarity search.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindSimilarToNodeAsync(nodeId, projectContext, cancellationToken);

    [McpServerTool(Name = "hybrid_search")]
    [Description(
        "Find semantically related code nodes using embeddings, then constrain the results to a graph neighborhood. " +
        "Use this when you want concept matching that stays near a specific subsystem or node. " +
        "Tests are excluded by default. Requires embeddings to be available and stored on code nodes.")]
    public Task<string> FindHybridSearchAsync(
        [Description("Free-text semantic query, e.g. 'retry policy'.")]
        string query,
        [Description("Optional node ID that anchors the graph neighborhood, e.g. 'OrderService'.")]
        string? nearNodeId = null,
        [Description("Maximum number of graph hops from the anchor node. Default 3.")]
        int maxHops = 3,
        [Description("Optional project name to narrow the search.")]
        string? projectContext = null,
        [Description("Exclude test files/namespaces by default.")]
        bool excludeTests = true,
        [Description("Maximum number of results to return. Default 10.")]
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        queryService.FindHybridSearchAsync(query, nearNodeId, maxHops, projectContext, excludeTests, limit, cancellationToken);

    [McpServerTool(Name = "find_implementation_patterns")]
    [Description(
        "Find structurally similar implementation slices for a requested feature or flow. " +
        "This blends lexical or embedding seeds with graph evidence such as entry points, application/domain behavior, contracts, repositories, external boundaries, and tests. " +
        "Use it when you want reusable implementation examples instead of only text similarity.")]
    public Task<string> FindImplementationPatternsAsync(
        [Description("Feature or flow query, e.g. 'invite acceptance flow'.")]
        string query,
        [Description("Optional project name to narrow the search.")]
        string? projectContext = null,
        [Description("Exclude test files and test-only anchors by default.")]
        bool excludeTests = true,
        [Description("Maximum number of ranked patterns to return. Default 5.")]
        int limit = 5,
        CancellationToken cancellationToken = default) =>
        queryService.FindImplementationPatternsAsync(query, projectContext, excludeTests, limit, cancellationToken);

    [McpServerTool(Name = "find_duplicate_candidates")]
    [Description(
        "Find duplicate-review candidates through the existing generic duplicate-analysis surface. " +
        "For Method/Class nodes it compares embedded code nodes semantically; for ExternalConcept it clusters indexed frontend style declarations by normalized value shape. " +
        "Supports project, node type, size, similarity, and test-exclusion filters, and keeps recommendations explainable.")]
    public Task<string> FindDuplicateCandidatesAsync(
        [Description("Optional project name to scope duplicate discovery.")]
        string? projectContext = null,
        [Description("Optional namespace substring filter for Method/Class nodes, or a frontend property/selector/file/value filter when nodeType is ExternalConcept.")]
        string? namespaceFilter = null,
        [Description("Optional node type filter. Valid values: Method, Class, ExternalConcept. Omit to include Method and Class.")]
        string? nodeType = null,
        [Description("Minimum line count for both nodes in a candidate pair. Default 5.")]
        int minLineCount = 5,
        [Description("Minimum cosine similarity from 0.0 to 1.0. Default 0.88.")]
        double minSimilarity = 0.88,
        [Description("Exclude test files/namespaces by default.")]
        bool excludeTests = true,
        CancellationToken cancellationToken = default) =>
        queryService.FindDuplicateCandidatesAsync(
            projectContext,
            namespaceFilter,
            nodeType,
            minLineCount,
            minSimilarity,
            excludeTests,
            cancellationToken);
}
