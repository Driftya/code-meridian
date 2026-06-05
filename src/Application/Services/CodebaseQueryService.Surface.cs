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

    public async Task<string> CheckGraphFreshnessAsync(
        string? query = null,
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = string.IsNullOrWhiteSpace(query) ? null : query,
                ProjectContext = projectContext,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken);

        if (nodes.Count == 0)
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}{(query is not null ? $" for `{query}`" : "")}.";

        var checks = nodes.Select(BuildFreshness).ToArray();
        var high = checks.Count(c => c.Confidence == "High");
        var medium = checks.Count(c => c.Confidence == "Medium");
        var low = checks.Count(c => c.Confidence == "Low");

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Freshness{(projectContext is not null ? $" - {projectContext}" : "")}");
        if (!string.IsNullOrWhiteSpace(query))
            sb.AppendLine($"**Query:** `{query}`");
        sb.AppendLine($"**Trust summary:** {high} High, {medium} Medium, {low} Low confidence\n");
        sb.AppendLine("| Confidence | Node | File exists | Line valid | Indexed/updated | Reason |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var check in checks)
        {
            var updated = check.Node.UpdatedAt?.ToString("u") ?? "unknown";
            sb.AppendLine($"| {check.Confidence} | `{check.Node.Name}` ({check.Node.Type}) | {YesNo(check.FileExists)} | {YesNo(check.LineRangeStillValid)} | {updated} | {check.Reason} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindGraphDriftAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 1000
            },
            cancellationToken);

        if (nodes.Count == 0)
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}. Run the indexer before checking drift.";

        var checks = nodes.Select(BuildFreshness).ToArray();
        var missingFiles = checks.Where(c => c.Node.FilePath is not null && c.FileExists == false).ToArray();
        var invalidLines = checks.Where(c => c.FileExists == true && c.LineRangeStillValid == false).ToArray();
        var missingTimestamps = checks.Where(c => c.Node.UpdatedAt is null).ToArray();
        var lowConfidence = checks.Count(c => c.Confidence == "Low");

        if (missingFiles.Length == 0 && invalidLines.Length == 0 && missingTimestamps.Length == 0)
            return $"Graph drift: low{(projectContext is not null ? $" for '{projectContext}'" : "")}. Files, line ranges, and update metadata look consistent.";

        var severity = lowConfidence > 25 || missingFiles.Length > 10 ? "high"
            : lowConfidence > 5 || missingFiles.Length > 0 || invalidLines.Length > 5 ? "moderate"
            : "low";

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Drift{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**Drift:** {severity}");
        sb.AppendLine($"**Signals:** {missingFiles.Length} nodes point to missing files, {invalidLines.Length} have invalid line ranges, {missingTimestamps.Length} lack update timestamps.\n");

        AppendDriftSection(sb, "Missing files", missingFiles, limit);
        AppendDriftSection(sb, "Invalid line ranges", invalidLines, limit);
        AppendDriftSection(sb, "Missing timestamps", missingTimestamps, limit);

        if (severity is "moderate" or "high")
            sb.AppendLine("Recommendation: run `codemeridian index . --project <ProjectName> --clear` before relying on exact implementation targets.");

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

    private static FreshnessCheck BuildFreshness(CodeNode node)
    {
        var fileExists = FileExists(node.FilePath);
        var lineRangeValid = LineRangeStillValid(node, fileExists);
        var confidence = fileExists == true && lineRangeValid != false ? "High"
            : fileExists == true ? "Medium"
            : "Low";
        var reason = confidence switch
        {
            "High" => "file exists and indexed line metadata is usable",
            "Medium" => "file exists but indexed line metadata looks incomplete or stale",
            _ => string.IsNullOrWhiteSpace(node.FilePath) ? "node has no file path" : "indexed file path was not found"
        };

        return new FreshnessCheck(node, fileExists, lineRangeValid, confidence, reason);
    }

    private static bool? FileExists(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return File.Exists(ResolveRepoPath(filePath));
    }

    private static bool? LineRangeStillValid(CodeNode node, bool? fileExists)
    {
        if (fileExists != true || node.LineNumber is null)
            return null;

        try
        {
            var path = ResolveRepoPath(node.FilePath!);
            var lineCount = File.ReadLines(path).Count();
            return node.LineNumber.Value > 0 && node.LineNumber.Value <= lineCount;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveRepoPath(string filePath) =>
        Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), filePath));

    private static string DescribeFreshness(FreshnessCheck check) =>
        $"{check.Confidence}: {check.Reason}";

    private static string YesNo(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "unknown"
    };

    private static string[] ParseConcepts(string? conceptsCsv) =>
        string.IsNullOrWhiteSpace(conceptsCsv)
            ? []
            : conceptsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TextMatches(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack)
        && !string.IsNullOrWhiteSpace(needle)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static void AppendDriftSection(StringBuilder sb, string title, IReadOnlyCollection<FreshnessCheck> checks, int limit)
    {
        if (checks.Count == 0)
            return;

        sb.AppendLine($"### {title} ({checks.Count})");
        foreach (var check in checks.Take(Math.Clamp(limit, 1, 100)))
            sb.AppendLine($"- `{check.Node.Name}` ({check.Node.Type}) - `{check.Node.FilePath ?? "no file"}` - {check.Reason}");
        sb.AppendLine();
    }

    private sealed record SurfaceCandidate(
        string FilePath,
        IReadOnlyList<CodeNode> Nodes,
        int Score,
        string Confidence,
        string Reason,
        FreshnessCheck Freshness);

    private sealed record FreshnessCheck(
        CodeNode Node,
        bool? FileExists,
        bool? LineRangeStillValid,
        string Confidence,
        string Reason);
}
