using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record ConnectionAnalysisResult(
    string ContractVersion,
    string FromId,
    string ToId,
    int MaxHops,
    bool PathFound,
    int HopCount,
    bool Truncated,
    IReadOnlyList<ConnectionNodeResult> Nodes,
    IReadOnlyList<ConnectionEdgeResult> Edges,
    IReadOnlyList<string> FrontendSignals)
{
    public string ToMarkdown(ContextDetailLevel detailLevel)
    {
        if (!PathFound)
        {
            return $"No path found between `{FromId}` and `{ToId}` within {MaxHops} hops. " +
                   "They may be in unconnected parts of the graph.";
        }

        if (detailLevel == ContextDetailLevel.Summary)
        {
            return $"Connection summary: `{FromId}` reaches `{ToId}` in {HopCount} hops through " +
                   $"{Nodes.Count} graph nodes.";
        }

        var edgesBySourceOrder = Edges.ToDictionary(edge => edge.Order);
        var builder = new StringBuilder();
        builder.AppendLine($"## Connection — `{FromId}` → `{ToId}`");
        builder.AppendLine($"Shortest path ({HopCount} hops):\n");

        foreach (var node in Nodes)
        {
            builder.Append($"**{node.Type}** `{node.Name}`");
            if (node.FilePath is not null)
                builder.Append($" ({node.FilePath})");
            if (edgesBySourceOrder.TryGetValue(node.Order, out var edge))
                builder.Append($"\n  —[{edge.Relationship}]→");
            builder.AppendLine();
        }

        if (FrontendSignals.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Frontend signals: {string.Join(", ", FrontendSignals)}.");
        }

        return builder.ToString();
    }
}
