using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> FindImplementationSurfaceAsync(
        string goal,
        string? conceptsCsv = null,
        string? projectContext = null,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var concepts = ParseConcepts(conceptsCsv);
        var queries = new[] { goal }.Concat(concepts).Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<CodeNode>();

        foreach (var query in queries)
        {
            var nodes = await codeGraph.QueryNodesAsync(
                new CodeGraphQuery
                {
                    SemanticQuery = query,
                    ProjectContext = projectContext,
                    Limit = 30
                },
                cancellationToken);

            candidates.AddRange(nodes);
        }

        if (candidates.Count == 0)
            return $"No implementation surface found for `{goal}`. Try a more specific goal, or re-index before relying on CodeMeridian for exact targets.";

        var ranked = candidates
            .Where(n => !string.IsNullOrWhiteSpace(n.FilePath))
            .GroupBy(n => n.FilePath!, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildSurfaceCandidate(group.Key, group, goal, concepts))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();

        if (ranked.Length == 0)
            return $"CodeMeridian found related nodes for `{goal}`, but none had file paths. Re-index with an up-to-date indexer before using this for implementation targeting.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Implementation Surface - `{goal}`");
        if (concepts.Length > 0)
            sb.AppendLine($"**Concepts:** {string.Join(", ", concepts.Select(c => $"`{c}`"))}");
        sb.AppendLine();
        sb.AppendLine("| Rank | Confidence | File | Likely methods/classes | Why | Freshness |");
        sb.AppendLine("|---:|---|---|---|---|---|");

        var rank = 1;
        foreach (var candidate in ranked)
        {
            var nodes = string.Join(", ", candidate.Nodes.Take(4).Select(n => $"`{n.Name}`"));
            var freshness = DescribeFreshness(candidate.Freshness);
            sb.AppendLine($"| {rank++} | {candidate.Confidence} | `{candidate.FilePath}` | {nodes} | {candidate.Reason} | {freshness} |");
        }

        sb.AppendLine();
        sb.AppendLine("CodeMeridian result: implementation targets are ranked from graph/document matches and local freshness checks. Verify source when confidence is not High.");

        return sb.ToString();
    }

    private static SurfaceCandidate BuildSurfaceCandidate(
        string filePath,
        IEnumerable<CodeNode> nodes,
        string goal,
        IReadOnlyCollection<string> concepts)
    {
        var nodeArray = nodes
            .DistinctBy(n => n.Id)
            .OrderBy(n => n.LineNumber ?? int.MaxValue)
            .ToArray();
        var score = nodeArray.Sum(node => ScoreSurfaceNode(node, goal, concepts));
        var freshness = BuildFreshness(nodeArray.First());
        var confidence = score >= 12 && freshness.Confidence != "Low" ? "High"
            : score >= 6 || freshness.Confidence == "Medium" ? "Medium"
            : "Low";
        var reason = BuildSurfaceReason(nodeArray, concepts);

        return new SurfaceCandidate(filePath, nodeArray, score, confidence, reason, freshness);
    }

    private static int ScoreSurfaceNode(CodeNode node, string goal, IReadOnlyCollection<string> concepts)
    {
        var score = node.Type switch
        {
            CodeNodeType.Method => 5,
            CodeNodeType.Class or CodeNodeType.Interface => 4,
            CodeNodeType.File => 3,
            _ => 1
        };

        if (TextMatches(node.Name, goal) || TextMatches(node.Summary, goal))
            score += 4;

        score += concepts.Count(concept => TextMatches(node.Name, concept) || TextMatches(node.Summary, concept) || TextMatches(node.FilePath, concept)) * 3;

        if (node.FilePath?.Contains("test", StringComparison.OrdinalIgnoreCase) == true)
            score += 1;

        return score;
    }

    private static string BuildSurfaceReason(IReadOnlyCollection<CodeNode> nodes, IReadOnlyCollection<string> concepts)
    {
        var methodCount = nodes.Count(n => n.Type == CodeNodeType.Method);
        var typeCount = nodes.Count(n => n.Type is CodeNodeType.Class or CodeNodeType.Interface);
        var conceptHits = concepts.Count(concept => nodes.Any(n => TextMatches(n.Name, concept) || TextMatches(n.FilePath, concept)));

        var parts = new List<string>();
        if (methodCount > 0) parts.Add($"{methodCount} method hits");
        if (typeCount > 0) parts.Add($"{typeCount} type hits");
        if (conceptHits > 0) parts.Add($"{conceptHits} concept matches");

        return parts.Count == 0 ? "related graph matches" : string.Join(", ", parts);
    }

    private static string[] ParseConcepts(string? conceptsCsv) =>
        string.IsNullOrWhiteSpace(conceptsCsv)
            ? []
            : conceptsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TextMatches(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack)
        && !string.IsNullOrWhiteSpace(needle)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private sealed record SurfaceCandidate(
        string FilePath,
        IReadOnlyList<CodeNode> Nodes,
        int Score,
        string Confidence,
        string Reason,
        FreshnessCheck Freshness);
}
