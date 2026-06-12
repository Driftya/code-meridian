using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

[McpServerToolType]
public sealed class KeywordTools(IKeywordGraphService keywordGraphService)
{
    [McpServerTool(Name = "find_related_knowledge")]
    [Description(
        "Find code, docs, diagnostics, and knowledge nodes that are lexically related to a source node through shared derived keywords. " +
        "Use this after rebuilding the keyword graph when structural edges alone do not explain a likely connection.")]
    public Task<string> FindRelatedKnowledgeAsync(
        [Description("ID of the source node to expand from.")]
        string sourceNodeId,
        [Description("Optional target kinds to include, e.g. ['KnowledgeDocument', 'Diagnostic', 'ApiEndpoint', 'Class', 'Method'].")]
        string[]? targetKinds = null,
        [Description("Minimum number of shared keywords required for a match.")]
        int? minimumSharedKeywords = null,
        [Description("Minimum lexical score required for a match.")]
        double? minimumScore = null,
        [Description("Maximum number of related nodes to return.")]
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        keywordGraphService.FindRelatedKnowledgeAsync(
            sourceNodeId,
            targetKinds,
            minimumSharedKeywords,
            minimumScore,
            limit,
            cancellationToken);

    [McpServerTool(Name = "rebuild_keyword_graph")]
    [Description(
        "Rebuild the derived keyword graph for an indexed project or for all projects. " +
        "This is a maintenance operation that tokenizes existing CodeNode and KnowledgeDocument text into shared Keyword nodes.")]
    public Task<string> RebuildKeywordGraphAsync(
        [Description("Optional project name to scope the rebuild. Omit to rebuild across all indexed projects.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        keywordGraphService.RebuildKeywordGraphAsync(projectContext, cancellationToken);
}
