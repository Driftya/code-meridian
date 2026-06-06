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
        sb.AppendLine("| Rank | Target confidence | File | Canonical IDs | Likely methods/classes | Why | Freshness |");
        sb.AppendLine("|---:|---|---|---|---|---|---|");

        var rank = 1;
        foreach (var candidate in ranked)
        {
            var nodes = string.Join(", ", candidate.Nodes.Take(4).Select(n => $"`{n.Name}`"));
            var ids = FormatCanonicalIds(candidate.Nodes);
            var freshness = DescribeFreshness(candidate.Freshness);
            sb.AppendLine($"| {rank++} | {candidate.TargetConfidence} | `{candidate.FilePath}` | {ids} | {nodes} | {candidate.Reason} | {freshness} |");
        }

        sb.AppendLine();
        sb.AppendLine("CodeMeridian result: implementation targets are ranked from graph/document matches and indexed metadata freshness checks. Use `resolve_exact_symbol` when target confidence is not exact.");

        return sb.ToString();
    }

    public async Task<string> PlanEditRouteAsync(
        string goal,
        string? conceptsCsv = null,
        string? projectContext = null,
        int limit = 8,
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

        var rankedNodes = candidates
            .Where(n => !string.IsNullOrWhiteSpace(n.FilePath))
            .DistinctBy(n => n.Id)
            .OrderByDescending(node => ScoreRouteNode(node, goal, concepts))
            .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 25))
            .ToArray();

        if (rankedNodes.Length == 0)
            return $"No edit route found for `{goal}`. Try `find_implementation_surface`, add more specific concepts, or re-index before relying on CodeMeridian for route planning.";

        var anchor = rankedNodes.FirstOrDefault(n => n.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface)
            ?? rankedNodes[0];
        var ctx = await codeGraph.GetContextForEditingAsync(anchor.Id, cancellationToken)
            ?? new EditingContext(null, [], [], []);
        var impact = await codeGraph.FindImpactAsync(anchor.Id, depth: 2, cancellationToken) ?? [];
        var downstream = await codeGraph.FindDownstreamAsync(anchor.Id, depth: 2, cancellationToken) ?? [];
        var relatedTests = await codeGraph.FindRelatedTestsAsync(anchor.Id, anchor.ProjectContext ?? projectContext, cancellationToken) ?? [];

        var routeNodes = rankedNodes
            .Concat(ctx.Node is null ? [] : [ctx.Node])
            .Concat(ctx.Interfaces)
            .Concat(ctx.Callees)
            .Concat(ctx.Callers)
            .Concat(downstream.Select(d => d.Node))
            .Concat(impact.Select(i => i.Node))
            .Concat(relatedTests.Select(t => t.Node))
            .Where(n => !string.IsNullOrWhiteSpace(n.FilePath))
            .DistinctBy(n => n.Id)
            .ToArray();

        var steps = BuildEditRouteSteps(routeNodes, anchor, goal);
        var routeConfidence = DescribeRouteConfidence(anchor, steps, ctx.Node is not null);

        var sb = new StringBuilder();
        sb.AppendLine($"## Change Route - `{goal}`");
        if (concepts.Length > 0)
            sb.AppendLine($"**Concepts:** {string.Join(", ", concepts.Select(c => $"`{c}`"))}");
        sb.AppendLine($"**Anchor:** `{anchor.Name}` ({anchor.Type}) - `{anchor.FilePath}`");
        sb.AppendLine($"**Route confidence:** {routeConfidence}");
        sb.AppendLine();
        sb.AppendLine("| Step | Edit route | Why this comes here | Primary targets |");
        sb.AppendLine("|---:|---|---|---|");

        var stepNumber = 1;
        foreach (var step in steps)
        {
            var targets = step.Nodes.Count == 0
                ? "No graph target found"
                : string.Join("<br>", step.Nodes.Take(4).Select(FormatRouteNode));
            sb.AppendLine($"| {stepNumber++} | {step.Title} | {step.Reason} | {targets} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Graph signals");
        sb.AppendLine($"- Implementation candidates: {rankedNodes.Length}");
        sb.AppendLine($"- Direct callers: {ctx.Callers.Count}");
        sb.AppendLine($"- Direct callees: {ctx.Callees.Count}");
        sb.AppendLine($"- Interfaces/contracts: {ctx.Interfaces.Count}");
        sb.AppendLine($"- Near impact nodes: {impact.Count}");
        sb.AppendLine($"- Near downstream nodes: {downstream.Count}");
        sb.AppendLine($"- Related tests: {relatedTests.Count}");
        sb.AppendLine();
        sb.AppendLine("> Use this as the edit itinerary. Run `build_minimal_context` on exact route targets before changing code.");

        return sb.ToString();
    }

    public async Task<string> ResolveExactSymbolAsync(
        string symbol,
        string? filePath = null,
        int? line = null,
        string? projectContext = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) && string.IsNullOrWhiteSpace(filePath))
            return "Provide a symbol name, file path, or both so CodeMeridian can resolve an exact node ID.";

        var query = new CodeGraphQuery
        {
            NameFilter = string.IsNullOrWhiteSpace(symbol) ? null : symbol,
            FilePathFilter = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
            ProjectContext = projectContext,
            Limit = Math.Clamp(limit * 5, 25, 200)
        };
        var nodes = await codeGraph.QueryNodesAsync(query, cancellationToken);
        var matches = nodes
            .Where(node => MatchesFileHint(node, filePath) || string.IsNullOrWhiteSpace(filePath))
            .Select(node => BuildSymbolResolution(node, symbol, filePath, line))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.LineDistance ?? int.MaxValue)
            .ThenBy(match => match.Node.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();

        if (matches.Length == 0)
        {
            var scope = projectContext is null ? string.Empty : $" in `{projectContext}`";
            var fileHint = string.IsNullOrWhiteSpace(filePath) ? string.Empty : $" and file `{filePath}`";
            return $"No exact symbol candidates found for `{symbol}`{fileHint}{scope}. Try `find_implementation_surface`, check graph freshness, or re-index with `codemeridian index . --project <ProjectName> --clear`.";
        }

        var exact = matches.Count(match => match.TargetConfidence == "exact");
        var fileOnly = matches.Count(match => match.TargetConfidence == "file-only");
        var heuristic = matches.Count(match => match.TargetConfidence == "heuristic");
        var stale = matches.Count(match => match.TargetConfidence == "stale");

        var sb = new StringBuilder();
        sb.AppendLine($"## Exact Symbol Resolution - `{symbol}`");
        if (!string.IsNullOrWhiteSpace(filePath))
            sb.AppendLine($"**File hint:** `{filePath}`");
        if (line is not null)
            sb.AppendLine($"**Line hint:** {line}");
        sb.AppendLine($"**Confidence summary:** {exact} exact, {fileOnly} file-only, {heuristic} heuristic, {stale} stale\n");
        sb.AppendLine("| Confidence | Canonical node ID | Type | Name | File | Line | Reason |");
        sb.AppendLine("|---|---|---|---|---|---:|---|");

        foreach (var match in matches)
        {
            var node = match.Node;
            var file = node.FilePath is null ? "-" : $"`{node.FilePath}`";
            var lineText = node.LineNumber?.ToString() ?? "";
            sb.AppendLine($"| {match.TargetConfidence} | `{node.Id}` | {node.Type} | `{node.Name}` | {file} | {lineText} | {match.Reason} |");
        }

        sb.AppendLine();
        sb.AppendLine("Use exact canonical node IDs with `get_context_for_editing`, `find_impact`, and `build_minimal_context`.");

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
        var targetConfidence = ResolveTargetConfidence(nodeArray, goal, concepts, freshness);
        var reason = BuildSurfaceReason(nodeArray, concepts);

        return new SurfaceCandidate(filePath, nodeArray, score, confidence, targetConfidence, reason, freshness);
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

    private static int ScoreRouteNode(CodeNode node, string goal, IReadOnlyCollection<string> concepts)
    {
        var score = ScoreSurfaceNode(node, goal, concepts);
        if (IsContractNode(node)) score += 2;
        if (IsTestNode(node)) score += 2;
        if (IsApiNode(node) || IsInfrastructureNode(node)) score += 1;
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
        var exactIds = nodes.Count(n => n.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface);
        if (exactIds > 0) parts.Add($"{exactIds} canonical IDs");

        return parts.Count == 0 ? "related graph matches" : string.Join(", ", parts);
    }

    private static string ResolveTargetConfidence(
        IReadOnlyCollection<CodeNode> nodes,
        string goal,
        IReadOnlyCollection<string> concepts,
        FreshnessCheck freshness)
    {
        if (freshness.Confidence == "Low")
            return "stale";

        var exactNode = nodes.Any(node =>
            node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface
            && (TextMatches(goal, node.Name) || concepts.Any(concept => TextMatches(node.Name, concept))));

        if (exactNode)
            return "exact";

        if (nodes.Any(node => node.Type == CodeNodeType.File || !string.IsNullOrWhiteSpace(node.FilePath)))
            return "file-only";

        return "heuristic";
    }

    private static string FormatCanonicalIds(IReadOnlyCollection<CodeNode> nodes)
    {
        var ids = nodes
            .Where(n => n.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface)
            .Take(3)
            .Select(n => $"`{n.Id}`")
            .ToArray();

        return ids.Length == 0 ? "-" : string.Join("<br>", ids);
    }

    private static bool MatchesFileHint(CodeNode node, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return true;
        if (string.IsNullOrWhiteSpace(node.FilePath))
            return false;

        return string.Equals(node.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
            || node.FilePath.EndsWith(filePath, StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(node.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private static SymbolResolution BuildSymbolResolution(CodeNode node, string symbol, string? filePath, int? line)
    {
        var freshness = BuildFreshness(node);
        var hasSymbol = !string.IsNullOrWhiteSpace(symbol);
        var nameExact = hasSymbol
            && (string.Equals(node.Name, symbol, StringComparison.OrdinalIgnoreCase)
                || node.Id.Contains(symbol, StringComparison.OrdinalIgnoreCase));
        var fileMatches = MatchesFileHint(node, filePath);
        var lineDistance = line is not null && node.LineNumber is not null
            ? Math.Abs(node.LineNumber.Value - line.Value)
            : (int?)null;
        var nearLine = lineDistance is not null && lineDistance <= Math.Max(node.LineCount ?? 1, 5);
        var typeScore = node.Type switch
        {
            CodeNodeType.Method => 5,
            CodeNodeType.Class or CodeNodeType.Interface => 4,
            CodeNodeType.File => 2,
            _ => 1
        };
        var score = typeScore
            + (nameExact ? 8 : TextMatches(node.Name, symbol) ? 4 : 0)
            + (fileMatches && !string.IsNullOrWhiteSpace(filePath) ? 4 : 0)
            + (nearLine ? 3 : 0)
            + (freshness.Confidence == "High" ? 2 : freshness.Confidence == "Medium" ? 1 : -4);
        var confidence = freshness.Confidence == "Low" ? "stale"
            : nameExact && (string.IsNullOrWhiteSpace(filePath) || fileMatches) && (line is null || nearLine) ? "exact"
            : fileMatches && !string.IsNullOrWhiteSpace(filePath) ? "file-only"
            : "heuristic";
        var reasons = new List<string>();
        if (nameExact) reasons.Add("name/id match");
        else if (TextMatches(node.Name, symbol)) reasons.Add("partial name match");
        if (!string.IsNullOrWhiteSpace(filePath) && fileMatches) reasons.Add("file match");
        if (nearLine) reasons.Add("near line hint");
        reasons.Add(DescribeFreshness(freshness));

        return new SymbolResolution(node, confidence, score, lineDistance, string.Join(", ", reasons));
    }

    private static string[] ParseConcepts(string? conceptsCsv) =>
        string.IsNullOrWhiteSpace(conceptsCsv)
            ? []
            : conceptsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TextMatches(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack)
        && !string.IsNullOrWhiteSpace(needle)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<EditRouteStep> BuildEditRouteSteps(
        IReadOnlyCollection<CodeNode> nodes,
        CodeNode anchor,
        string goal)
    {
        var contracts = nodes.Where(IsContractNode).ToArray();
        var appDomain = nodes.Where(n => !IsContractNode(n) && (IsApplicationNode(n) || IsDomainNode(n))).ToArray();
        var infrastructure = nodes.Where(IsInfrastructureNode).ToArray();
        var composition = nodes.Where(n => IsCompositionNode(n) || IsApiNode(n)).ToArray();
        var tests = nodes.Where(IsTestNode).ToArray();
        var fallback = nodes
            .Where(n => !contracts.Contains(n)
                && !appDomain.Contains(n)
                && !infrastructure.Contains(n)
                && !composition.Contains(n)
                && !tests.Contains(n))
            .OrderByDescending(n => ScoreRouteNode(n, goal, []))
            .ToArray();

        return
        [
            new EditRouteStep(
                "Contract / port",
                "Start with interfaces and public contracts so downstream edits have a stable shape.",
                contracts.Length == 0 && IsContractNode(anchor) ? [anchor] : contracts),
            new EditRouteStep(
                "Application / domain behavior",
                "Change the use case or domain behavior before wiring outer layers.",
                appDomain.Length == 0 && !IsInfrastructureNode(anchor) && !IsTestNode(anchor) ? [anchor] : appDomain),
            new EditRouteStep(
                "Infrastructure implementation",
                "Update adapters and persistence/API implementations after the contract is clear.",
                infrastructure),
            new EditRouteStep(
                "Composition and API entry points",
                "Update DI registrations and endpoint/controller surfaces after implementations exist.",
                composition),
            new EditRouteStep(
                "Tests and verification",
                "Finish with direct or heuristic tests that protect the route.",
                tests),
            new EditRouteStep(
                "Fallback graph targets",
                "Inspect remaining graph matches if the route still feels incomplete.",
                fallback)
        ];
    }

    private static bool IsContractNode(CodeNode node) =>
        node.Type == CodeNodeType.Interface
        || TextMatches(node.Name, "interface")
        || TextMatches(node.Name, "port")
        || TextMatches(node.FilePath, "port")
        || TextMatches(node.FilePath, "abstractions");

    private static bool IsApplicationNode(CodeNode node) =>
        TextMatches(node.FilePath, "/Application/")
        || TextMatches(node.FilePath, "\\Application\\")
        || TextMatches(node.Namespace, ".Application");

    private static bool IsDomainNode(CodeNode node) =>
        TextMatches(node.FilePath, "/Domain/")
        || TextMatches(node.FilePath, "\\Domain\\")
        || TextMatches(node.FilePath, "/Core/")
        || TextMatches(node.FilePath, "\\Core\\")
        || TextMatches(node.Namespace, ".Domain")
        || TextMatches(node.Namespace, ".Core");

    private static bool IsInfrastructureNode(CodeNode node) =>
        TextMatches(node.FilePath, "/Infrastructure/")
        || TextMatches(node.FilePath, "\\Infrastructure\\")
        || TextMatches(node.Namespace, ".Infrastructure");

    private static bool IsCompositionNode(CodeNode node) =>
        TextMatches(node.Name, "Program")
        || TextMatches(node.Name, "DependencyInjection")
        || TextMatches(node.FilePath, "Program.cs")
        || TextMatches(node.FilePath, "DependencyInjection");

    private static bool IsApiNode(CodeNode node) =>
        TextMatches(node.FilePath, "/Api/")
        || TextMatches(node.FilePath, "\\Api\\")
        || TextMatches(node.FilePath, "/Controllers/")
        || TextMatches(node.FilePath, "\\Controllers\\")
        || TextMatches(node.Name, "Endpoint")
        || TextMatches(node.Name, "Controller");

    private static bool IsTestNode(CodeNode node) =>
        TextMatches(node.FilePath, "test")
        || TextMatches(node.Namespace, "test")
        || TextMatches(node.Name, "test");

    private static string FormatRouteNode(CodeNode node)
    {
        var line = node.LineNumber is null ? string.Empty : $":{node.LineNumber}";
        return $"`{node.Name}` - `{node.FilePath}{line}`";
    }

    private static string DescribeRouteConfidence(
        CodeNode anchor,
        IReadOnlyCollection<EditRouteStep> steps,
        bool exactContextFound)
    {
        var nonEmptySteps = steps.Count(step => step.Nodes.Count > 0);
        if (exactContextFound && nonEmptySteps >= 4)
            return "High - exact anchor plus graph targets across most route stages";
        if (!string.IsNullOrWhiteSpace(anchor.FilePath) && nonEmptySteps >= 2)
            return "Medium - graph found an anchor and partial route coverage";
        return "Low - route is mostly heuristic; re-index or add concepts for better targeting";
    }

    private sealed record SurfaceCandidate(
        string FilePath,
        IReadOnlyList<CodeNode> Nodes,
        int Score,
        string Confidence,
        string TargetConfidence,
        string Reason,
        FreshnessCheck Freshness);

    private sealed record SymbolResolution(
        CodeNode Node,
        string TargetConfidence,
        int Score,
        int? LineDistance,
        string Reason);

    private sealed record EditRouteStep(
        string Title,
        string Reason,
        IReadOnlyList<CodeNode> Nodes);
}
