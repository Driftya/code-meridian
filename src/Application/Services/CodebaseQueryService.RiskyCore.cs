using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public async Task<string> FindBridgesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        IReadOnlyList<(CodeNode Node, double Score)> betweenness;
        IReadOnlyList<(CodeNode Node, double Score)> pageRank;
        IReadOnlyList<(CodeNode Node, int ResultingComponentCount)> articulationPoints;
        IReadOnlyList<(CodeNode Source, CodeNode Target, IReadOnlyList<long> RemainingSizes)> bridgeEdges;
        try
        {
            var betweennessTask = codeGraph.GetBetweennessAsync(projectContext, limit: 20, cancellationToken);
            var pageRankTask = codeGraph.GetPageRankAsync(projectContext, limit: 20, cancellationToken);
            var articulationTask = codeGraph.GetArticulationPointsAsync(projectContext, limit: 20, cancellationToken);
            var bridgeEdgesTask = codeGraph.GetBridgeEdgesAsync(projectContext, limit: 30, cancellationToken);

            await Task.WhenAll(betweennessTask, pageRankTask, articulationTask, bridgeEdgesTask);

            betweenness = await betweennessTask;
            pageRank = await pageRankTask;
            articulationPoints = await articulationTask;
            bridgeEdges = await bridgeEdgesTask;
        }
        catch (Exception ex)
        {
            return $"Risky core analysis failed: {ex.Message}\n" +
                   "Ensure the Graph Data Science plugin is installed.";
        }

        if (betweenness.Count == 0
            && pageRank.Count == 0
            && articulationPoints.Count == 0
            && bridgeEdges.Count == 0)
        {
            return $"No risky core nodes detected{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "The graph may have no edges yet.";
        }

        var filteredBridgeEdges = bridgeEdges
            .Where(edge => ShouldIncludeRiskyCoreNode(edge.Source) && ShouldIncludeRiskyCoreNode(edge.Target))
            .ToArray();
        var bridgeEdgeCounts = bridgeEdges
            .Where(edge => ShouldIncludeRiskyCoreNode(edge.Source) && ShouldIncludeRiskyCoreNode(edge.Target))
            .SelectMany(edge => new[] { edge.Source.Id, edge.Target.Id })
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var nodesById = betweenness.Select(item => item.Node)
            .Concat(pageRank.Select(item => item.Node))
            .Concat(articulationPoints.Select(item => item.Node))
            .Concat(filteredBridgeEdges.SelectMany(edge => new[] { edge.Source, edge.Target }))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var betweennessById = betweenness.ToDictionary(item => item.Node.Id, item => item.Score, StringComparer.Ordinal);
        var pageRankById = pageRank.ToDictionary(item => item.Node.Id, item => item.Score, StringComparer.Ordinal);
        var articulationById = articulationPoints.ToDictionary(item => item.Node.Id, item => item.ResultingComponentCount, StringComparer.Ordinal);
        var maxBetweenness = betweennessById.Count == 0 ? 0 : betweennessById.Max(item => item.Value);
        var maxPageRank = pageRankById.Count == 0 ? 0 : pageRankById.Max(item => item.Value);
        var maxArticulation = articulationById.Count == 0 ? 0 : articulationById.Max(item => item.Value);
        var maxBridgeEdges = bridgeEdgeCounts.Count == 0 ? 0 : bridgeEdgeCounts.Max(item => item.Value);

        var candidates = nodesById.Values
            .Select(node =>
            {
                var normalizedBetweenness = NormalizeRiskSignal(betweennessById.GetValueOrDefault(node.Id), maxBetweenness);
                var normalizedPageRank = NormalizeRiskSignal(pageRankById.GetValueOrDefault(node.Id), maxPageRank);
                var normalizedArticulation = NormalizeRiskSignal(articulationById.GetValueOrDefault(node.Id), maxArticulation);
                var normalizedBridgeEdges = NormalizeRiskSignal(bridgeEdgeCounts.GetValueOrDefault(node.Id), maxBridgeEdges);
                var baseScore = (int)Math.Round(
                    normalizedBetweenness * 40 +
                    normalizedPageRank * 22 +
                    normalizedArticulation * 23 +
                    normalizedBridgeEdges * 15,
                    MidpointRounding.AwayFromZero);

                return new RiskyCoreCandidate(
                    node,
                    betweennessById.GetValueOrDefault(node.Id),
                    pageRankById.GetValueOrDefault(node.Id),
                    articulationById.GetValueOrDefault(node.Id),
                    bridgeEdgeCounts.GetValueOrDefault(node.Id),
                    normalizedBetweenness,
                    normalizedPageRank,
                    normalizedArticulation,
                    normalizedBridgeEdges,
                    baseScore);
            })
            .Where(candidate => ShouldIncludeRiskyCoreNode(candidate.Node))
            .Where(candidate => candidate.BaseScore > 0)
            .OrderByDescending(candidate => candidate.BaseScore)
            .ThenBy(candidate => NodeDisplayRank(candidate.Node))
            .ThenBy(candidate => candidate.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var contexts = await Task.WhenAll(candidates.Select(candidate => codeGraph.GetContextForEditingAsync(candidate.Node.Id, cancellationToken)));
        var contextsById = contexts
            .Where(context => context?.Node is not null)
            .ToDictionary(context => context!.Node!.Id, context => context!, StringComparer.Ordinal);
        var ranked = candidates
            .Select(candidate =>
            {
                var context = contextsById.GetValueOrDefault(candidate.Node.Id) ?? new EditingContext(candidate.Node, [], [], []);
                var layers = GetConnectedLayers(candidate.Node, context);
                var freshness = BuildFreshness(candidate.Node);
                return new RiskyCoreReportRow(
                    candidate,
                    context,
                    layers,
                    freshness.Confidence,
                    CalculateRiskyCoreScore(candidate, layers.Count, freshness.Confidence));
            })
            .OrderByDescending(row => row.Score)
            .ThenBy(row => NodeDisplayRank(row.Candidate.Node))
            .ThenBy(row => row.Candidate.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"## Risky Core Nodes{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine("Classic bridge nodes plus other structurally risky core nodes ranked from shared graph signals across Roslyn and TsIndexer data.\n");
        sb.AppendLine("| Rank | Risk | Type | Name | Structural reason | Layers | Callers / deps | Next tool | Confidence | File |");
        sb.AppendLine("|------|------|------|------|-------------------|--------|----------------|-----------|------------|------|");

        var rank = 1;
        foreach (var row in ranked)
        {
            var node = row.Candidate.Node;
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "-";
            var reason = EscapeTableCell(DescribeRiskyCoreReason(row.Candidate, row.Layers.Count));
            var layers = row.Layers.Count == 0 ? "unknown" : EscapeTableCell(string.Join(" -> ", row.Layers));
            var counts = $"{row.Context.Callers.Count} / {row.Context.Callees.Count}";
            var nextTool = BuildRiskyCoreNextTool(row.Candidate, row.Context);
            sb.AppendLine($"| {rank++} | {DescribeRiskLabel(row.Score)} | {node.Type} | `{node.Name}` | {reason} | {layers} | {counts} | `{nextTool}` | {row.Confidence} | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Bridge edges");
        foreach (var edge in filteredBridgeEdges.Take(6))
        {
            sb.AppendLine(
                $"- `{edge.Source.Name}` -> `{edge.Target.Name}` splits components [{string.Join(", ", edge.RemainingSizes.Select(size => size.ToString(CultureInfo.InvariantCulture)))}]");
        }

        if (filteredBridgeEdges.Length == 0)
            sb.AppendLine("- none detected");

        sb.AppendLine();
        sb.AppendLine("> Signals blend PageRank, betweenness, articulation points, and bridge edges over shared structural relationships such as Calls, Uses, DependsOn, Implements, Reads, Writes, and frontend dependency edges.");
        sb.AppendLine("> Confidence reflects indexed metadata freshness, not mathematical certainty. Use the suggested next tool before refactoring a risky core node.");

        return sb.ToString();
    }

    private bool ShouldIncludeRiskyCoreNode(CodeNode node) =>
        node.Type != CodeNodeType.File
        && AllowsProfile(node, AnalysisProfile.DesignSmells)
        && !LooksLikeDocumentationPath(node.FilePath ?? string.Empty);

    private static double NormalizeRiskSignal(double value, double maxValue) =>
        maxValue <= 0 ? 0 : value / maxValue;

    private static double NormalizeRiskSignal(int value, int maxValue) =>
        maxValue <= 0 ? 0 : (double)value / maxValue;

    private static int CalculateRiskyCoreScore(RiskyCoreCandidate candidate, int connectedLayerCount, string confidence)
    {
        var score = candidate.BaseScore + Math.Max(0, connectedLayerCount - 1) * 4;
        if (confidence == "Low")
            score -= 18;
        else if (confidence == "Medium")
            score -= 6;

        return Math.Clamp(score, 0, 100);
    }

    private static string DescribeRiskLabel(int score) =>
        score switch
        {
            >= 75 => "Critical",
            >= 55 => "High",
            >= 35 => "Moderate",
            _ => "Watch"
        };

    private static string DescribeRiskyCoreReason(RiskyCoreCandidate candidate, int connectedLayerCount)
    {
        var reasons = new List<string>();
        if (candidate.ArticulationComponents > 0)
            reasons.Add($"splits graph into {candidate.ArticulationComponents} component(s)");
        if (candidate.BridgeEdgeCount > 0)
            reasons.Add($"touches {candidate.BridgeEdgeCount} bridge edge(s)");
        if (candidate.NormalizedBetweenness >= 0.6)
            reasons.Add("top betweenness connector");
        if (candidate.NormalizedPageRank >= 0.6)
            reasons.Add("high PageRank influence");
        if (connectedLayerCount >= 3)
            reasons.Add("cross-layer path");

        return reasons.Count == 0
            ? "ranked by mixed structural signals"
            : string.Join(", ", reasons.Take(3));
    }

    private static string BuildRiskyCoreNextTool(RiskyCoreCandidate candidate, EditingContext context)
    {
        if (candidate.ArticulationComponents > 0 || candidate.BridgeEdgeCount >= 2)
            return "find_impact";

        if (context.Callers.Count == 0 && context.Callees.Count == 0)
            return "get_context_for_editing";

        return context.Callers.Count > 0
            ? "find_test_shield"
            : "find_impact";
    }

    private sealed record RiskyCoreCandidate(
        CodeNode Node,
        double BetweennessScore,
        double PageRankScore,
        int ArticulationComponents,
        int BridgeEdgeCount,
        double NormalizedBetweenness,
        double NormalizedPageRank,
        double NormalizedArticulation,
        double NormalizedBridgeEdges,
        int BaseScore);

    private sealed record RiskyCoreReportRow(
        RiskyCoreCandidate Candidate,
        EditingContext Context,
        IReadOnlyList<string> Layers,
        string Confidence,
        int Score);
}
