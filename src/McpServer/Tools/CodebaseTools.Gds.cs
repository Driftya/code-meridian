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
}
