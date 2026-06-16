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
        "Find structurally important bridge nodes by combining GDS betweenness centrality with direct call-graph context. " +
        "Reports likely connected layers, a bridge-risk note, and metadata confidence so you can spot small nodes " +
        "that connect otherwise separate parts of the system. Requires Neo4j Graph Data Science plugin.")]
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

    [McpServerTool(Name = "find_duplicate_candidates")]
    [Description(
        "Find duplicate-code review candidates by comparing embedded method/class nodes semantically. " +
        "Groups similar methods/classes by score, filters by project, namespace, node type, and size, " +
        "excludes tests by default, and reports lightweight refactor risk using fan-in and direct test callers. " +
        "Requires backend embeddings to be enabled and indexed.")]
    public Task<string> FindDuplicateCandidatesAsync(
        [Description("Optional project name to scope duplicate discovery.")]
        string? projectContext = null,
        [Description("Optional namespace substring filter, e.g. 'Payments' or 'Infrastructure.Graph'.")]
        string? namespaceFilter = null,
        [Description("Optional node type filter. Valid values: Method, Class. Omit to include both.")]
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
