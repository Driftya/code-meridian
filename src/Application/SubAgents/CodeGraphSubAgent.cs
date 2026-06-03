using CodeMeridian.Core.Agents;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.SubAgents;

/// <summary>
/// Queries the Neo4j code knowledge graph to answer structural questions
/// about classes, methods, call graphs, and dependency relationships.
/// </summary>
public sealed class CodeGraphSubAgent(ICodeGraphRepository repository) : ISubAgent
{
    public string Name => "CodeGraph";
    public string Description => "Queries the code knowledge graph for structural, dependency, and relationship information.";
    public IReadOnlyList<string> Capabilities => ["code-structure", "dependency-analysis", "call-graph", "inheritance"];

    public async Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var query = new CodeGraphQuery
        {
            SemanticQuery = request.Query,
            ProjectContext = context.ProjectContext,
            Limit = 15
        };

        var nodes = await repository.QueryNodesAsync(query, cancellationToken);

        if (nodes.Count == 0)
            return new AgentResponse
            {
                Content = "No code graph data found. Ingest your codebase first via POST /api/v1/knowledge/ingest.",
                AgentName = Name
            };

        var summaryTasks = nodes.Take(5)
            .Select(n => repository.GetSubgraphSummaryAsync(n.Id, cancellationToken));

        var summaries = await Task.WhenAll(summaryTasks);

        var content = $"Found {nodes.Count} relevant code elements:\n\n" +
                      string.Join("\n---\n", summaries.Where(s => !string.IsNullOrEmpty(s)));

        return new AgentResponse
        {
            Content = content,
            AgentName = Name,
            Sources = [.. nodes.Select(n => n.FilePath ?? n.Name)]
        };
    }
}
