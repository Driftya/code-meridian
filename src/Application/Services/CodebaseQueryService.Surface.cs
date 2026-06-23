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
        var frontendGoal = LooksLikeFrontendGoal(goal, concepts);
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

            if (frontendGoal || nodes.Any(IsFrontendGraphNode))
                candidates.AddRange(await ExpandFrontendSurfaceCandidatesAsync(nodes, cancellationToken));
        }

        if (candidates.Count == 0)
            return $"No implementation surface found for `{goal}`. Try a more specific goal, or re-index before relying on CodeMeridian for exact targets.";

        var ranked = candidates
            .Where(n => !string.IsNullOrWhiteSpace(n.FilePath))
            .GroupBy(n => n.FilePath!, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildSurfaceCandidate("mcp__CodeMeridian.find_implementation_surface", group.Key, group, goal, concepts))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();

        if (ranked.Length == 0)
            return $"CodeMeridian found related nodes for `{goal}`, but none had file paths. Re-index with an up-to-date indexer before using this for implementation targeting.";

        var pruned = PruneSurfaceCandidates(ranked, limit);

        var sb = new StringBuilder();
        sb.AppendLine($"## Implementation Surface - `{goal}`");
        if (concepts.Length > 0)
            sb.AppendLine($"**Concepts:** {string.Join(", ", concepts.Select(c => $"`{c}`"))}");
        sb.AppendLine($"**Pruned result:** {pruned.PrimaryTargets.Count} primary edit target(s), {pruned.ContextOnlyTargets.Count} context-only target(s)");
        sb.AppendLine();
        sb.AppendLine("### Primary Edit Targets");
        sb.AppendLine("| Rank | Target confidence | File | Canonical IDs | Likely methods/classes | Why | Freshness |");
        sb.AppendLine("|---:|---|---|---|---|---|---|");

        var rank = 1;
        foreach (var candidate in pruned.PrimaryTargets)
        {
            var nodes = string.Join(", ", candidate.Nodes.Take(4).Select(n => $"`{n.Name}`"));
            var ids = FormatCanonicalIds(candidate.Nodes);
            var freshness = DescribeFreshness(candidate.Freshness);
            sb.AppendLine($"| {rank++} | {candidate.TargetConfidence} | `{candidate.FilePath}` | {ids} | {nodes} | {candidate.Reason} | {freshness} |");
        }

        if (pruned.ContextOnlyTargets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Context-Only Targets");
            sb.AppendLine("| File | Why excluded from primary | Target confidence | Likely methods/classes | Freshness |");
            sb.AppendLine("|---|---|---|---|---|");

            foreach (var item in pruned.ContextOnlyTargets)
            {
                var nodes = string.Join(", ", item.Candidate.Nodes.Take(4).Select(n => $"`{n.Name}`"));
                var freshness = DescribeFreshness(item.Candidate.Freshness);
                sb.AppendLine($"| `{item.Candidate.FilePath}` | {item.ExclusionReason} | {item.Candidate.TargetConfidence} | {nodes} | {freshness} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("CodeMeridian result: primary edit targets are pruned from broader graph matches using file-role heuristics, exact-symbol signals, and indexed metadata freshness. Use `resolve_exact_symbol` when target confidence is not exact.");

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
            .Where(node => ShouldIncludeRouteCandidate(node, goal, concepts))
            .DistinctBy(n => n.Id)
            .OrderByDescending(node => ScoreRouteNode(node, goal, concepts))
            .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 25))
            .ToArray();

        if (rankedNodes.Length == 0)
        {
            rankedNodes = candidates
                .Where(n => !string.IsNullOrWhiteSpace(n.FilePath))
                .DistinctBy(n => n.Id)
                .OrderByDescending(node => ScoreRouteNode(node, goal, concepts))
                .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 25))
                .ToArray();
        }

        if (rankedNodes.Length == 0)
            return $"No edit route found for `{goal}`. Try `find_implementation_surface`, add more specific concepts, or re-index before relying on CodeMeridian for route planning.";

        var anchor = SelectRouteAnchor(rankedNodes);
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

    private SurfaceCandidate BuildSurfaceCandidate(
        string toolName,
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
        var feedback = EvaluateSurfaceFeedback(toolName, filePath);
        score += feedback.ScoreAdjustment;
        var confidence = score >= 12 && freshness.Confidence != "Low" ? "High"
            : score >= 6 || freshness.Confidence == "Medium" ? "Medium"
            : "Low";
        var targetConfidence = ResolveTargetConfidence(nodeArray, goal, concepts, freshness, feedback.Tool);
        var reason = BuildSurfaceReason(nodeArray, concepts, feedback);

        return new SurfaceCandidate(filePath, nodeArray, score, confidence, targetConfidence, reason, freshness);
    }

    private PrunedSurfaceResult PruneSurfaceCandidates(
        IReadOnlyList<SurfaceCandidate> ranked,
        int limit)
    {
        var primary = new List<SurfaceCandidate>();
        var context = new List<ContextOnlySurfaceCandidate>();

        foreach (var candidate in ranked)
        {
            var exclusionReason = GetPrimaryExclusionReason(candidate, primary.Count > 0);
            if (exclusionReason is null)
            {
                primary.Add(candidate);
                continue;
            }

            context.Add(new ContextOnlySurfaceCandidate(candidate, exclusionReason));
        }

        var primaryLimit = Math.Clamp(limit, 1, 5);
        if (primary.Count == 0 && ranked.Count > 0)
        {
            var promoted = ranked[0];
            primary.Add(promoted);
            context.RemoveAll(item => string.Equals(item.Candidate.FilePath, promoted.FilePath, StringComparison.OrdinalIgnoreCase));
        }

        return new PrunedSurfaceResult(
            primary.Take(primaryLimit).ToArray(),
            context.Take(Math.Clamp(limit, 1, 12)).ToArray());
    }

    private static int ScoreSurfaceNode(CodeNode node, string goal, IReadOnlyCollection<string> concepts)
    {
        var score = node.Type switch
        {
            CodeNodeType.Method => 5,
            CodeNodeType.Class or CodeNodeType.Interface => 4,
            CodeNodeType.ExternalConcept => 4,
            CodeNodeType.File => 3,
            _ => 1
        };

        if (TextMatches(node.Name, goal) || TextMatches(node.Summary, goal))
            score += 4;

        score += concepts.Count(concept => TextMatches(node.Name, concept) || TextMatches(node.Summary, concept) || TextMatches(node.FilePath, concept)) * 3;

        if (LooksLikeFrontendGoal(goal, concepts) && IsFrontendGraphNode(node))
            score += node.Type == CodeNodeType.ExternalConcept ? 4 : 3;

        if (node.FilePath?.Contains("test", StringComparison.OrdinalIgnoreCase) == true)
            score += 1;

        return score;
    }

    private int ScoreRouteNode(CodeNode node, string goal, IReadOnlyCollection<string> concepts)
    {
        var score = ScoreSurfaceNode(node, goal, concepts);
        if (IsContractNode(node)) score += 2;
        if (IsConfiguredTestNode(node)) score += 2;
        if (IsApiNode(node) || IsInfrastructureNode(node)) score += 1;
        return score;
    }

    private bool ShouldIncludeRouteCandidate(CodeNode node, string goal, IReadOnlyCollection<string> concepts)
    {
        if (LooksLikeDocumentationPath(node.FilePath ?? string.Empty))
            return false;

        var role = ResolveFileRole(node);
        if (role is IndexedFileRole.Test
            or IndexedFileRole.Generated
            or IndexedFileRole.BuildArtifact
            or IndexedFileRole.Snapshot
            or IndexedFileRole.Migration)
            return false;

        if (role == IndexedFileRole.Configuration)
            return TextMatches(goal, "config") || concepts.Any(concept => TextMatches(concept, "config"));

        return true;
    }

    private static string BuildSurfaceReason(
        IReadOnlyCollection<CodeNode> nodes,
        IReadOnlyCollection<string> concepts,
        SurfaceFeedback feedback)
    {
        var methodCount = nodes.Count(n => n.Type == CodeNodeType.Method);
        var typeCount = nodes.Count(n => n.Type is CodeNodeType.Class or CodeNodeType.Interface);
        var frontendSignals = nodes.Count(IsFrontendGraphNode);
        var conceptHits = concepts.Count(concept => nodes.Any(n => TextMatches(n.Name, concept) || TextMatches(n.FilePath, concept)));

        var parts = new List<string>();
        if (methodCount > 0) parts.Add($"{methodCount} method hits");
        if (typeCount > 0) parts.Add($"{typeCount} type hits");
        if (frontendSignals > 0) parts.Add($"{frontendSignals} frontend graph matches");
        if (conceptHits > 0) parts.Add($"{conceptHits} concept matches");
        var exactIds = nodes.Count(n => n.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.ExternalConcept);
        if (exactIds > 0) parts.Add($"{exactIds} canonical IDs");
        if (!string.IsNullOrWhiteSpace(feedback.Reason)) parts.Add(feedback.Reason);

        return parts.Count == 0 ? "related graph matches" : string.Join(", ", parts);
    }

    private static string ResolveTargetConfidence(
        IReadOnlyCollection<CodeNode> nodes,
        string goal,
        IReadOnlyCollection<string> concepts,
        FreshnessCheck freshness,
        ToolPrecisionSnapshot? feedback)
    {
        if (freshness.Confidence == "Low")
            return "stale";

        var exactNode = nodes.Any(node =>
            node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.ExternalConcept
            && (TextMatches(goal, node.Name) || concepts.Any(concept => TextMatches(node.Name, concept))));

        if (exactNode)
            return "exact";

        if (feedback is not null && feedback.FileOnlyTargets > 0 && nodes.Any(node => node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface))
            return "heuristic";

        if (nodes.Any(node => node.Type == CodeNodeType.File || !string.IsNullOrWhiteSpace(node.FilePath)))
            return "file-only";

        return "heuristic";
    }

    private string? GetPrimaryExclusionReason(SurfaceCandidate candidate, bool exactOrHeuristicPrimaryExists)
    {
        var role = ResolveCandidateRole(candidate);

        if (candidate.Freshness.Confidence == "Low")
            return "stale metadata lowers trust";

        if (LooksLikeDocumentationPath(candidate.FilePath))
            return "documentation file is context, not the edit surface";

        if (role == IndexedFileRole.Test)
            return "test target is verification context, not the primary implementation";

        if (role is IndexedFileRole.Generated or IndexedFileRole.BuildArtifact or IndexedFileRole.Snapshot or IndexedFileRole.Migration)
            return $"{role.ToString().ToLowerInvariant()} file should not be the primary edit surface";

        if (role == IndexedFileRole.Configuration)
            return "configuration file is supporting context unless the goal is explicitly config-only";

        if (candidate.Nodes.All(IsContractNode))
            return "contract or port shapes the change but is not the concrete implementation";

        if (candidate.TargetConfidence == "file-only" && !HasEditReadySymbol(candidate))
            return "broad file match without an edit-ready symbol anchor";

        if (candidate.TargetConfidence == "file-only" && exactOrHeuristicPrimaryExists)
            return "broader file-only match was pruned behind stronger symbol-level targets";

        return null;
    }

    private IndexedFileRole ResolveCandidateRole(SurfaceCandidate candidate)
    {
        var rankedRole = candidate.Nodes
            .Select(ResolveFileRole)
            .GroupBy(role => role)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => CandidateRoleRank(group.Key))
            .Select(group => group.Key)
            .FirstOrDefault();

        return rankedRole;
    }

    private static int CandidateRoleRank(IndexedFileRole role) =>
        role switch
        {
            IndexedFileRole.Source => 0,
            IndexedFileRole.Unknown => 1,
            IndexedFileRole.Configuration => 2,
            IndexedFileRole.Test => 3,
            IndexedFileRole.Generated => 4,
            IndexedFileRole.Migration => 5,
            IndexedFileRole.Snapshot => 6,
            IndexedFileRole.BuildArtifact => 7,
            _ => 8
        };

    private static bool HasEditReadySymbol(SurfaceCandidate candidate) =>
        candidate.Nodes.Any(node => node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Struct or CodeNodeType.ApiEndpoint or CodeNodeType.ConfigurationKey)
        && candidate.Nodes.Any(node => node.Type is not CodeNodeType.File);

    private static bool LooksLikeDocumentationPath(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "README.md", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "TODO.md", StringComparison.OrdinalIgnoreCase);

    private static string FormatCanonicalIds(IReadOnlyCollection<CodeNode> nodes)
    {
        var ids = nodes
            .Where(n => n.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.ExternalConcept)
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

    private async Task<IReadOnlyCollection<CodeNode>> ExpandFrontendSurfaceCandidatesAsync(
        IReadOnlyCollection<CodeNode> nodes,
        CancellationToken cancellationToken)
    {
        var anchors = nodes
            .Where(IsFrontendGraphNode)
            .DistinctBy(node => node.Id)
            .Take(6)
            .ToArray();

        if (anchors.Length == 0)
            return [];

        var expanded = new List<CodeNode>();
        foreach (var anchor in anchors)
        {
            var impact = await codeGraph.FindImpactAsync(anchor.Id, depth: 2, cancellationToken);
            expanded.AddRange(impact.Select(item => item.Node).Where(node => !string.IsNullOrWhiteSpace(node.FilePath)));

            var downstream = await codeGraph.FindDownstreamAsync(anchor.Id, depth: 2, cancellationToken);
            expanded.AddRange(downstream.Select(item => item.Node).Where(node => !string.IsNullOrWhiteSpace(node.FilePath)));
        }

        return expanded
            .DistinctBy(node => node.Id)
            .ToArray();
    }

    private static bool LooksLikeFrontendGoal(string goal, IReadOnlyCollection<string> concepts)
    {
        var terms = new[] { goal }.Concat(concepts);
        return terms.Any(term =>
            TextMatches(term, "frontend")
            || TextMatches(term, "html")
            || TextMatches(term, "css")
            || TextMatches(term, "scss")
            || TextMatches(term, "jsx")
            || TextMatches(term, "tsx")
            || TextMatches(term, "selector")
            || TextMatches(term, "stylesheet")
            || TextMatches(term, "style")
            || TextMatches(term, "class")
            || TextMatches(term, "id")
            || TextMatches(term, "token")
            || TextMatches(term, "variable"));
    }

    private static bool IsFrontendGraphNode(CodeNode node) =>
        IsFrontendExternalConcept(node) || IsFrontendFile(node);

    private static bool IsFrontendExternalConcept(CodeNode node) =>
        node.Type == CodeNodeType.ExternalConcept
        && node.Properties.TryGetValue("externalKind", out var kind)
        && kind is "CssClass" or "CssId" or "CssSelector" or "CssVariable";

    private static bool IsFrontendFile(CodeNode node)
    {
        if (node.Properties.TryGetValue("frontendRole", out var frontendRole)
            && !string.IsNullOrWhiteSpace(frontendRole))
            return true;

        return node.FilePath is not null
            && (node.FilePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || node.FilePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || node.FilePath.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)
                || node.FilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || node.FilePath.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<EditRouteStep> BuildEditRouteSteps(
        IReadOnlyCollection<CodeNode> nodes,
        CodeNode anchor,
        string goal)
    {
        var contracts = nodes.Where(IsContractNode).ToArray();
        var appDomain = nodes.Where(n => !IsContractNode(n) && (IsApplicationNode(n) || IsDomainNode(n))).ToArray();
        var infrastructure = nodes.Where(IsInfrastructureNode).ToArray();
        var composition = nodes.Where(n => IsCompositionNode(n) || IsApiNode(n)).ToArray();
        var tests = nodes.Where(IsConfiguredTestNode).ToArray();
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
                appDomain.Length == 0 && !IsInfrastructureNode(anchor) && !IsConfiguredTestNode(anchor) ? [anchor] : appDomain),
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

    private bool IsCompositionNode(CodeNode node) =>
        analysisOptions.Ranking.InfrastructureNames.Any(name =>
            TextMatches(node.Name, name) || TextMatches(node.FilePath, name));

    private static bool IsApiNode(CodeNode node) =>
        node.Type == CodeNodeType.ApiEndpoint
        || TextMatches(node.FilePath, "/Api/")
        || TextMatches(node.FilePath, "\\Api\\")
        || TextMatches(node.FilePath, "/Controllers/")
        || TextMatches(node.FilePath, "\\Controllers\\")
        || TextMatches(node.Name, "Endpoint")
        || TextMatches(node.Name, "Controller");

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

    private CodeNode SelectRouteAnchor(IReadOnlyList<CodeNode> rankedNodes) =>
        rankedNodes.FirstOrDefault(node =>
            node.Type is CodeNodeType.Method or CodeNodeType.Class
            && (IsApplicationNode(node) || IsDomainNode(node)))
        ?? rankedNodes.FirstOrDefault(node => node.Type == CodeNodeType.Interface)
        ?? rankedNodes.FirstOrDefault(node => node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface)
        ?? rankedNodes[0];

    private sealed record SurfaceCandidate(
        string FilePath,
        IReadOnlyList<CodeNode> Nodes,
        int Score,
        string Confidence,
        string TargetConfidence,
        string Reason,
        FreshnessCheck Freshness);

    private sealed record PrunedSurfaceResult(
        IReadOnlyList<SurfaceCandidate> PrimaryTargets,
        IReadOnlyList<ContextOnlySurfaceCandidate> ContextOnlyTargets);

    private sealed record ContextOnlySurfaceCandidate(
        SurfaceCandidate Candidate,
        string ExclusionReason);

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
