using System.ComponentModel;
using System.Net.Http.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Extensions;
using CodeMeridian.Sdk.Models;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

/// <summary>
/// Tools for managing and calling registered project-specific sub-agents.
/// Any project can register its own agent and CodeMeridian will route Copilot
/// requests to it when relevant.
/// </summary>
[McpServerToolType]
public sealed class ExtensionTools(
    IExtensionRegistry registry,
    IHttpClientFactory httpClientFactory,
    ICodeGraphRepository codeGraph)
{
    [McpServerTool(Name = "list_project_agents")]
    [Description(
        "List all external project agents currently registered with CodeMeridian. " +
        "Shows each agent's name, capabilities, and health status. " +
        "Use this to understand what specialized knowledge is available.")]
    public string ListProjectAgents()
    {
        var agents = registry.GetAll();

        if (agents.Count == 0)
            return "No project agents registered. Use register_project_agent to connect one.";

        var lines = agents.Select(a =>
            $"- **{a.Name}** [{(a.IsHealthy ? "healthy" : "unhealthy")}]\n" +
            $"  {a.Description}\n" +
            $"  Capabilities: {string.Join(", ", a.Capabilities)}\n" +
            $"  Endpoint: {a.Endpoint}");

        return string.Join("\n", lines);
    }

    [McpServerTool(Name = "register_project_agent")]
    [Description(
        "Register an external project agent so CodeMeridian can call it. " +
        "The external agent must expose a POST endpoint that accepts a JSON body " +
        "with a 'query' field and returns a JSON body with a 'content' field. " +
        "After registration, Copilot can route queries to that agent via call_project_agent.")]
    public string RegisterProjectAgent(
        [Description("Unique name for the agent, e.g. 'PaymentsService'")]
        string name,
        [Description("Short description of what this agent knows")]
        string description,
        [Description("Full URL of the agent's POST endpoint, e.g. 'http://payments-agent:5001/ask'")]
        string endpoint,
        [Description("Comma-separated list of capabilities, e.g. 'payments,billing,invoices'")]
        string capabilities)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return $"Invalid endpoint URL: {endpoint}";

        var extension = new AgentExtension
        {
            Name = name,
            Description = description,
            Endpoint = uri,
            Capabilities = [.. capabilities.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
        };

        registry.Register(extension);
        return $"Agent '{name}' registered with capabilities: {string.Join(", ", extension.Capabilities)}";
    }

    [McpServerTool(Name = "unregister_project_agent")]
    [Description("Remove a registered project agent.")]
    public string UnregisterProjectAgent(
        [Description("Name of the agent to remove")]
        string name)
    {
        registry.Unregister(name);
        return $"Agent '{name}' unregistered.";
    }

    [McpServerTool(Name = "call_project_agent")]
    [Description(
        "Forward a query directly to a specific registered project agent. " +
        "Use this when you need domain-specific context that a project agent specializes in. " +
        "For example: call the 'PaymentsService' agent with questions about billing logic.")]
    public async Task<string> CallProjectAgentAsync(
        [Description("Name of the registered agent to call")]
        string agentName,
        [Description("The question or query to send to the agent")]
        string query,
        [Description("Optional project context to include")]
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var agent = registry.Get(agentName);
        if (agent is null)
            return $"Agent '{agentName}' not found. Use list_project_agents to see available agents.";

        var client = httpClientFactory.CreateClient("CodeMeridianExtension");

        try
        {
            var request = new AgentRequest { Query = query, ProjectContext = projectContext };
            var response = await client.PostAsJsonAsync(agent.Endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AgentResponse>(cancellationToken: cancellationToken);

            if (result is null) return "Agent returned an empty response.";

            agent.IsHealthy = true;
            return result.Content;
        }
        catch (Exception ex)
        {
            agent.IsHealthy = false;
            return $"Agent '{agentName}' call failed: {ex.Message}. The agent may be offline.";
        }
    }

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
        // Upsert the external concept as a CodeNode with type ExternalConcept
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

        // Resolve edge direction
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
