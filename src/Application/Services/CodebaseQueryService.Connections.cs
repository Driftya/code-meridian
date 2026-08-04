namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> FindConnectionAsync(
        string fromId,
        string toId,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var result = await FindConnectionResultAsync(fromId, toId, cancellationToken);
        return result.ToMarkdown(detailLevel);
    }

    public async Task<ConnectionAnalysisResult> FindConnectionResultAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default)
    {
        const int maxHops = 10;
        var path = await codeGraph.FindConnectionAsync(fromId, toId, cancellationToken);
        var frontendSignals = SummarizeFrontendRelationships(path.Select(step => step.ViaRelationship));
        var nodes = path
            .Select((step, order) => new ConnectionNodeResult(
                order,
                step.Node.Id,
                step.Node.Name,
                step.Node.Type.ToString(),
                step.Node.Namespace,
                step.Node.FilePath,
                step.Node.LineNumber,
                step.Node.ProjectContext))
            .ToArray();
        var relationshipsAreIncoming = path.Count > 1
            && string.IsNullOrWhiteSpace(path[0].ViaRelationship)
            && !string.IsNullOrWhiteSpace(path[^1].ViaRelationship);
        var edges = Enumerable.Range(0, Math.Max(0, path.Count - 1))
            .Select(order => new
            {
                Order = order,
                Relationship = relationshipsAreIncoming
                    ? path[order + 1].ViaRelationship
                    : path[order].ViaRelationship
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Relationship))
            .Select(item => new ConnectionEdgeResult(
                item.Order,
                path[item.Order].Node.Id,
                path[item.Order + 1].Node.Id,
                item.Relationship!))
            .ToArray();

        return new ConnectionAnalysisResult(
            ContractVersion: "1.0",
            FromId: fromId,
            ToId: toId,
            MaxHops: maxHops,
            PathFound: path.Count > 0,
            HopCount: Math.Max(0, path.Count - 1),
            Truncated: false,
            Nodes: nodes,
            Edges: edges,
            FrontendSignals: frontendSignals);
    }
}
