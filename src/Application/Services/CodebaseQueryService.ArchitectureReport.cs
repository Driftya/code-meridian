using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    private async Task<string> GetArchitectureWeatherReportCoreAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var nodeCount = await codeGraph.CountCodeNodesAsync(projectContext, cancellationToken);
        if (nodeCount == 0)
        {
            var projectHint = await BuildProjectContextHintAsync(projectContext, cancellationToken);
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}.{projectHint} Run the indexer before generating an architecture report.";
        }

        var callEdgeCountTask = codeGraph.CountCallEdgesAsync(projectContext, cancellationToken);
        var cyclesTask = codeGraph.FindCyclesAsync(projectContext, cancellationToken);
        var violationsTask = codeGraph.FindArchitectureViolationsAsync(projectContext, cancellationToken);
        var coverageGapsTask = codeGraph.FindCoverageGapsAsync(projectContext, cancellationToken);
        var freshnessNodesTask = codeGraph.QueryNodesAsync(new CodeGraphQuery
        {
            ProjectContext = projectContext,
            Limit = 1000
        }, cancellationToken);

        await Task.WhenAll(callEdgeCountTask, cyclesTask, violationsTask, coverageGapsTask, freshnessNodesTask);

        var bridgeResult = await ReadOptionalReportSectionAsync("get_betweenness", projectContext,
            () => codeGraph.GetBetweennessAsync(projectContext, limit: 10, cancellationToken), cancellationToken);
        var bridgeNodes = bridgeResult.Value ?? [];
        var freshness = SummarizeFreshness(freshnessNodesTask.Result);
        var score = CalculateWeatherScore(
            cyclesTask.Result.Count,
            violationsTask.Result.Count,
            coverageGapsTask.Result.Count,
            freshness.LowConfidence,
            bridgeNodes.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"# Architecture Weather Report{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine();
        if (bridgeResult.Value is not null)
        {
            sb.AppendLine($"**Weather:** {DescribeWeather(score)}");
            sb.AppendLine($"**Score:** {score.ToString(CultureInfo.InvariantCulture)}/100");
        }
        else
            sb.AppendLine("**Weather and score:** unavailable; projection coverage is incomplete.");
        sb.AppendLine();
        sb.AppendLine("| Signal | Count |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Code nodes | {nodeCount.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Call relationships | {callEdgeCountTask.Result.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Namespace cycles | {cyclesTask.Result.Count.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Architecture violations | {violationsTask.Result.Count.ToString("N0", CultureInfo.InvariantCulture)} |");
        if (bridgeResult.Value is not null)
            sb.AppendLine($"| Bridge nodes | {bridgeNodes.Count.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Untested methods/classes | {coverageGapsTask.Result.Count.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Low-confidence freshness nodes | {freshness.LowConfidence.ToString("N0", CultureInfo.InvariantCulture)} |");
        sb.AppendLine();
        sb.AppendLine("## Freshness");
        sb.AppendLine($"High confidence: **{freshness.HighConfidence.ToString("N0", CultureInfo.InvariantCulture)}**");
        sb.AppendLine($"Medium confidence: **{freshness.MediumConfidence.ToString("N0", CultureInfo.InvariantCulture)}**");
        sb.AppendLine($"Low confidence: **{freshness.LowConfidence.ToString("N0", CultureInfo.InvariantCulture)}**");

        if (bridgeResult.UnavailableReason is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Bridge nodes: unavailable ({bridgeResult.UnavailableReason})");
        }
        else if (bridgeNodes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Top Bridge Nodes");
            foreach (var (node, scoreValue) in bridgeNodes.Take(5))
                sb.AppendLine($"- `{node.Name}` ({node.Type}) - `{node.FilePath ?? "no file"}` - score {scoreValue.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        return sb.ToString();
    }

    private static FreshnessSummary SummarizeFreshness(IReadOnlyCollection<CodeNode> nodes)
    {
        var checks = nodes.Select(BuildFreshness).ToArray();
        return new FreshnessSummary(
            checks.Count(check => check.Confidence == "High"),
            checks.Count(check => check.Confidence == "Medium"),
            checks.Count(check => check.Confidence == "Low"));
    }

    private static int CalculateWeatherScore(
        int cycleCount,
        int violationCount,
        int coverageGapCount,
        int lowConfidenceCount,
        int bridgeNodeCount)
    {
        var penalty =
            Math.Min(25, cycleCount * 5) +
            Math.Min(25, violationCount * 5) +
            Math.Min(20, coverageGapCount / 5) +
            Math.Min(20, lowConfidenceCount / 10) +
            Math.Min(10, bridgeNodeCount);

        return Math.Clamp(100 - penalty, 0, 100);
    }

    private static string DescribeWeather(int score) =>
        score switch
        {
            >= 90 => "Clear",
            >= 75 => "Partly cloudy",
            >= 50 => "Cloudy",
            >= 25 => "Storm watch",
            _ => "Severe storm"
        };

    private sealed record FreshnessSummary(int HighConfidence, int MediumConfidence, int LowConfidence);
}
