using System.ComponentModel;
using CodeMeridian.Core.CodeGraph;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

/// <summary>
/// Tools for linking external concepts into the code graph.
/// </summary>
[McpServerToolType]
public sealed class ExtensionTools(ICodeGraphRepository codeGraph)
{
    [McpServerTool(Name = "link_external_concept")]
    [Description(
        "Create or update a node representing an external concept (database table, API endpoint, Kafka topic, " +
        "external service, etc.) and draw a directed relationship edge to or from an existing code node. " +
        "Use this to weave findings from external MCP tools (e.g. database tools, API introspection) " +
        "into the code knowledge graph, enabling cross-tool impact analysis. " +
        "For example: after a DB tool reveals that 'OrderService.SaveAsync' writes to the 'orders' table, " +
        "call this tool to record that relationship so future impact queries surface it.")]
    public async Task<string> LinkExternalConceptAsync(
        [Description("The existing code node ID to link from or to. Format: 'Type:FullyQualifiedName'")]
        string codeNodeId,
        [Description("A unique ID for the external concept. E.g. 'db:orders', 'api:POST /payments', 'topic:order-events'")]
        string externalConceptId,
        [Description("Human-readable name for the external concept. E.g. 'orders table', 'POST /payments'")]
        string externalConceptName,
        [Description("Category of the external concept: 'DatabaseTable', 'ApiEndpoint', 'MessageTopic', 'ExternalService', or 'Other'")]
        string conceptType = "Other",
        [Description("Relationship type: 'Reads', 'Writes', 'Calls', 'PublishesTo', 'SubscribesTo', 'DependsOn'. Defaults to 'DependsOn'")]
        string relationshipType = "DependsOn",
        [Description("Direction: 'outgoing' = codeNode→external, 'incoming' = external→codeNode")]
        string direction = "outgoing",
        [Description("Optional project context for the external concept node")]
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<CodeNodeType>(conceptType, ignoreCase: true, out var nodeType))
            nodeType = CodeNodeType.ExternalConcept;

        var externalNode = new CodeNode
        {
            Id = externalConceptId,
            Name = externalConceptName,
            Type = nodeType,
            ProjectContext = projectContext,
            Properties = new Dictionary<string, string> { ["source"] = "linked-by-copilot" }
        };

        await codeGraph.UpsertNodeAsync(externalNode, cancellationToken);

        var (sourceId, targetId) = direction.Equals("incoming", StringComparison.OrdinalIgnoreCase)
            ? (externalConceptId, codeNodeId)
            : (codeNodeId, externalConceptId);

        if (!Enum.TryParse<CodeEdgeType>(relationshipType, ignoreCase: true, out var edgeType))
            edgeType = CodeEdgeType.DependsOn;

        var edge = new CodeEdge
        {
            Id = $"{sourceId}→{targetId}:{relationshipType}",
            SourceId = sourceId,
            TargetId = targetId,
            Type = edgeType
        };

        await codeGraph.UpsertEdgeAsync(edge, cancellationToken);

        return $"Linked: `{sourceId}` –[{relationshipType}]→ `{targetId}`. " +
               $"The external concept '{externalConceptName}' is now in the graph and will appear in impact analysis queries.";
    }
}
