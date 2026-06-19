using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

// ── Structural analytics formatters ──────────────────────────────────────────
// SRP: this file is responsible solely for formatting graph-analytics results.
// Core CRUD methods live in CodebaseQueryService.cs.
// GDS algorithm formatters live in CodebaseQueryService.Gds.cs.

public partial class CodebaseQueryService
{
    public async Task<string> FindImpactAsync(
        string nodeId,
        int depth = 5,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        bool includeConfidence = false,
        CancellationToken cancellationToken = default)
    {
        if (includeConfidence)
            return await FindImpactWithConfidenceAsync(nodeId, depth, detailLevel, cancellationToken);

        var results = await codeGraph.FindImpactAsync(nodeId, depth, cancellationToken);

        if (results.Count == 0)
            return $"No callers found for `{nodeId}` within {depth} hops. " +
                   "The node may not exist in the graph or has no inbound dependencies.";

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Impact summary for `{nodeId}`: {results.Count} affected code elements within {depth} hops. " +
                   $"Nearest distance: {results.Min(r => r.Distance)}. Farthest distance: {results.Max(r => r.Distance)}.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Impact Analysis — `{nodeId}`");
        sb.AppendLine($"**{results.Count}** code elements would be affected by changing this (up to {depth} hops):\n");
        sb.AppendLine("| Distance | Type | Name | File |");
        sb.AppendLine("|----------|------|------|------|");

        foreach (var (node, dist) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {dist} | {node.Type} | `{node.Name}` | {file} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindHotspotsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindHotspotsAsync(projectContext, limit: 15, cancellationToken);

        if (results.Count == 0)
            return $"No hotspots found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Ensure the codebase has been indexed with relationship data.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Coupling Hotspots{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Nodes with the most incoming dependencies — highest risk to change:\n");
        sb.AppendLine("| Rank | Fan-in | Type | Name | File |");
        sb.AppendLine("|------|--------|------|------|------|");

        var rank = 1;
        foreach (var (node, fanIn) in results
                     .OrderBy(item => NodeDisplayRank(item.Node))
                     .ThenByDescending(item => item.FanIn))
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {rank++} | {fanIn} | {node.Type} | `{node.Name}` | {file} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindConnectionAsync(
        string fromId,
        string toId,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var path = await codeGraph.FindConnectionAsync(fromId, toId, cancellationToken);

        if (path.Count == 0)
            return $"No path found between `{fromId}` and `{toId}` within 10 hops. " +
                   "They may be in unconnected parts of the graph.";

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Connection summary: `{fromId}` reaches `{toId}` in {path.Count - 1} hops through " +
                   $"{path.Count} code nodes.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Connection — `{fromId}` → `{toId}`");
        sb.AppendLine($"Shortest path ({path.Count - 1} hops):\n");

        for (var i = 0; i < path.Count; i++)
        {
            var (node, via) = path[i];
            sb.Append($"**{node.Type}** `{node.Name}`");
            if (node.FilePath is not null) sb.Append($" ({node.FilePath})");
            if (via is not null) sb.Append($"\n  —[{via}]→");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> FindUnreferencedAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindUnreferencedAsync(projectContext, cancellationToken);

        if (results.Count == 0)
            return $"No unreferenced methods or classes found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Everything appears to be referenced.";

        var grouped = results.GroupBy(n => n.Type).OrderBy(g => g.Key.ToString());

        var sb = new StringBuilder();
        sb.AppendLine($"## Unreferenced Code{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** methods/classes with no incoming Calls, Uses, or Contains edges (dead code candidates):\n");

        foreach (var group in grouped)
        {
            sb.AppendLine($"### {group.Key}s ({group.Count()})");
            foreach (var node in group)
            {
                var loc = node.FilePath is not null
                    ? $"`{node.FilePath}`{(node.LineNumber.HasValue ? $":{node.LineNumber}" : "")}"
                    : "—";
                sb.AppendLine($"- `{node.Name}` — {loc}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("> Note: entry points, event handlers, and DI-registered types may appear here even if actively used.");

        return sb.ToString();
    }

    public async Task<string> FindCrossProjectDependenciesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindCrossProjectDependenciesAsync(projectContext, cancellationToken);

        if (results.Count == 0)
            return $"No cross-project dependencies found{(projectContext is not null ? $" involving '{projectContext}'" : "")}. " +
                   "All edges appear to be within single projects.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Cross-Project Dependencies{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** edges cross project boundaries:\n");
        sb.AppendLine("| From Project | Source | Rel | Target | To Project |");
        sb.AppendLine("|-------------|--------|-----|--------|-----------|");

        foreach (var (source, target, rel) in results)
        {
            sb.AppendLine($"| `{source.ProjectContext}` | `{source.Name}` ({source.Type}) | {rel} | `{target.Name}` ({target.Type}) | `{target.ProjectContext}` |");
        }

        return sb.ToString();
    }

    public async Task<string> FindCoverageGapsAsync(
        string? projectContext = null,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindCoverageGapsAsync(projectContext, cancellationToken);
        var filteredResults = FilterNodesByProfile(results, AnalysisProfile.CoverageGaps);

        if (filteredResults.Count == 0)
            return $"No coverage gaps found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "All production classes and methods appear to be called from test namespaces.";

        var rankedResults = RankNodesForDisplay(filteredResults).ToArray();
        var grouped = rankedResults.GroupBy(n => n.Type).OrderBy(g => g.Key.ToString()).ToArray();

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Coverage gap summary{(projectContext is not null ? $" for '{projectContext}'" : "")}: " +
                   string.Join(", ", grouped.Select(g => $"{g.Count()} {g.Key}"));

        var sb = new StringBuilder();
        sb.AppendLine($"## Test Coverage Gaps{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{rankedResults.Length}** production types/methods with no test calling them, ranked by likely risk:\n");

        foreach (var group in grouped)
        {
            sb.AppendLine($"### {group.Key}s ({group.Count()})");
            foreach (var node in group)
            {
                var loc = node.FilePath is not null
                    ? $"`{node.FilePath}`{(node.LineNumber.HasValue ? $":{node.LineNumber}" : "")}"
                    : "—";
                sb.AppendLine($"- `{node.Name}` — {loc}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("> Detection is heuristic: test namespaces or file paths are identified by containing 'test'. " +
                      "DI entry points and abstract types may appear here.");

        return sb.ToString();
    }

    public async Task<string> FindTestShieldAsync(
        string nodeId,
        string? projectContext = null,
        int depth = 2,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var ctx = await codeGraph.GetContextForEditingAsync(nodeId, cancellationToken);
        if (ctx.Node is null)
            return $"Node `{nodeId}` not found in the graph. Run `query_codebase` or `resolve_exact_symbol` to find the correct target before checking its test shield.";

        var scope = ctx.Node.ProjectContext ?? projectContext;
        var impact = await codeGraph.FindImpactAsync(ctx.Node.Id, Math.Clamp(depth, 1, 8), cancellationToken);
        var directAndHeuristicTargetTests = await codeGraph.FindRelatedTestsAsync(ctx.Node.Id, scope, cancellationToken);
        var targetDependencyKeys = BuildDependencyKeys(ctx.Callees.Concat(ctx.Interfaces));

        var pathNodes = new[] { ctx.Node }
            .Concat(ctx.Callers)
            .Concat(impact.Select(i => i.Node))
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield) && !IsConfiguredTestNode(node))
            .DistinctBy(node => node.Id)
            .Take(Math.Clamp(limit * 2, 10, 100))
            .ToArray();

        var directShield = directAndHeuristicTargetTests
            .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield))
            .DistinctBy(node => node.Id)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();

        var primaryShield = new Dictionary<string, ShieldEntry>(StringComparer.Ordinal);
        var secondaryShield = new Dictionary<string, ShieldEntry>(StringComparer.Ordinal);
        foreach (var match in directAndHeuristicTargetTests.Where(match => !match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AllowsProfile(match.Node, AnalysisProfile.TestShield))
                continue;

            secondaryShield[match.Node.Id] = CreateShieldEntry(
                match.Node,
                ctx.Node,
                "heuristic",
                targetDependencyKeys,
                targetDependencyKeys,
                isExactCallerPath: false);
        }

        var unshielded = new List<CodeNode>();
        foreach (var pathNode in pathNodes)
        {
            var pathContext = pathNode.Id == ctx.Node.Id
                ? ctx
                : await codeGraph.GetContextForEditingAsync(pathNode.Id, cancellationToken);
            var relatedTests = pathNode.Id == ctx.Node.Id
                ? directAndHeuristicTargetTests
                : await codeGraph.FindRelatedTestsAsync(pathNode.Id, scope, cancellationToken);

            var hasShield = false;
            foreach (var match in relatedTests)
            {
                if (!AllowsProfile(match.Node, AnalysisProfile.TestShield))
                    continue;

                hasShield = true;
                if (pathNode.Id == ctx.Node.Id && match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (directShield.Any(test => test.Id == match.Node.Id))
                    continue;

                var shieldEntry = CreateShieldEntry(
                    match.Node,
                    pathNode,
                    match.MatchType,
                    targetDependencyKeys,
                    BuildDependencyKeys(pathContext.Callees.Concat(pathContext.Interfaces)),
                    isExactCallerPath: pathNode.Id != ctx.Node.Id);
                var targetBucket = IsPrimaryShieldCandidate(shieldEntry)
                    ? primaryShield
                    : secondaryShield;
                targetBucket[match.Node.Id] = shieldEntry;
                if (targetBucket == primaryShield)
                    secondaryShield.Remove(match.Node.Id);
            }

            if (!hasShield)
                unshielded.Add(pathNode);
        }

        var rankedPrimaryShield = primaryShield.Values
            .OrderByDescending(ScoreShieldEntry)
            .ThenBy(entry => entry.TestNode.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();
        var rankedSecondaryShield = secondaryShield.Values
            .Where(entry => primaryShield.ContainsKey(entry.TestNode.Id) is false)
            .OrderByDescending(ScoreShieldEntry)
            .ThenBy(entry => entry.TestNode.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();
        var suggestedCommand = BuildSuggestedTestCommand(directShield, rankedPrimaryShield);

        var sb = new StringBuilder();
        sb.AppendLine($"## Test Shield Map - `{ctx.Node.Name}`");
        sb.AppendLine($"**Target:** {ctx.Node.Type} `{ctx.Node.Name}`");
        sb.AppendLine($"**File:** `{ctx.Node.FilePath ?? "—"}`{(ctx.Node.LineNumber.HasValue ? $":{ctx.Node.LineNumber}" : "")}");
        sb.AppendLine($"**Path depth:** {Math.Clamp(depth, 1, 8)}");
        sb.AppendLine($"**Shield summary:** {directShield.Length} direct, {rankedPrimaryShield.Length} primary, {rankedSecondaryShield.Length} secondary, {unshielded.Count} unshielded path nodes");
        sb.AppendLine();

        sb.AppendLine($"### Direct test shield ({directShield.Length})");
        if (directShield.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var test in directShield)
                sb.AppendLine($"- **{test.Type}** `{test.Name}`{FormatLocation(test)} — direct `Calls` edge to `{ctx.Node.Name}`");
        }
        sb.AppendLine();

        sb.AppendLine($"### Primary verification tests ({rankedPrimaryShield.Length})");
        if (rankedPrimaryShield.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var shield in rankedPrimaryShield)
            {
                sb.AppendLine($"- **{shield.TestNode.Type}** `{shield.TestNode.Name}`{FormatLocation(shield.TestNode)} — {shield.Reason}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"### Secondary shield awareness ({rankedSecondaryShield.Length})");
        if (rankedSecondaryShield.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var shield in rankedSecondaryShield)
                sb.AppendLine($"- **{shield.TestNode.Type}** `{shield.TestNode.Name}`{FormatLocation(shield.TestNode)} — {shield.Reason}");
        }
        sb.AppendLine();

        sb.AppendLine($"### Unshielded path nodes ({unshielded.Count})");
        if (unshielded.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var node in unshielded.Take(Math.Clamp(limit, 1, 50)))
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{FormatLocation(node)} — no direct or heuristic related tests found");
        }
        sb.AppendLine();

        sb.AppendLine("### Suggested test command");
        sb.AppendLine(suggestedCommand is null ? "- none" : $"- `{suggestedCommand}`");
        sb.AppendLine();

        sb.AppendLine("> Direct shield means a test directly calls the target. Primary verification tests protect the exact caller path or adjacent slice dependencies first. Secondary shield awareness keeps broader or heuristic tests visible without mixing them into the first-run verification set. Unshielded path nodes are the best seams for new characterization tests before changing behavior.");

        return sb.ToString();
    }

    public async Task<string> FindRecentlyChangedAsync(
        string? projectContext = null,
        string window = "24h",
        CancellationToken cancellationToken = default)
    {
        var timeSpan = ParseWindow(window);
        var results = await codeGraph.FindRecentlyChangedAsync(projectContext, timeSpan, cancellationToken);

        if (results.Count == 0)
            return $"No nodes created or updated in the last {window}" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Note: timestamps are only tracked for nodes indexed after this feature was deployed.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Recently Changed — last {window}{(projectContext is not null ? $" in {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** nodes changed:\n");
        sb.AppendLine("| When | Change | Type | Name | File |");
        sb.AppendLine("|------|--------|------|------|------|");

        foreach (var (node, changedAt, changeType) in results)
        {
            var age = DateTimeOffset.UtcNow - changedAt;
            var ageStr = age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago"
                : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago"
                : $"{(int)age.TotalDays}d ago";
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {ageStr} | {changeType} | {node.Type} | `{node.Name}` | {file} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindLargeNodesAsync(
        string? projectContext = null,
        int classThreshold = 400,
        int methodThreshold = 40,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindLargeNodesAsync(projectContext, classThreshold, methodThreshold, cancellationToken);
        var filteredResults = FilterNodesByProfile(results, AnalysisProfile.DesignSmells);

        if (filteredResults.Count == 0)
            return $"No large classes (>{classThreshold} lines) or methods (>{methodThreshold} lines) found" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Re-index with an up-to-date indexer to populate line counts.";

        var grouped = filteredResults.GroupBy(n => n.Type).OrderBy(g => g.Key.ToString()).ToArray();

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Large node summary{(projectContext is not null ? $" for '{projectContext}'" : "")}: " +
                   string.Join(", ", grouped.Select(g => $"{g.Count()} {g.Key}")) +
                   $". Largest element: `{filteredResults.OrderByDescending(n => n.LineCount).First().Name}`.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Large Node Analysis{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{filteredResults.Count}** oversized elements (classes >{classThreshold} lines, methods >{methodThreshold} lines):\n");

        foreach (var group in grouped)
        {
            sb.AppendLine($"### {group.Key}s ({group.Count()})");
            sb.AppendLine("| Lines | Name | File |");
            sb.AppendLine("|-------|------|------|");
            foreach (var node in group.OrderByDescending(n => n.LineCount))
            {
                var file = node.FilePath is not null
                    ? $"`{node.FilePath}`{(node.LineNumber.HasValue ? $":{node.LineNumber}" : "")}"
                    : "—";
                sb.AppendLine($"| {node.LineCount} | `{node.Name}` | {file} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("> Refactor candidates: split classes along SRP boundaries, extract long methods into helpers.");

        return sb.ToString();
    }

    public async Task<string> GetContextForEditingAsync(
        string nodeId,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var ctx = await codeGraph.GetContextForEditingAsync(nodeId, cancellationToken);

        if (ctx.Node is null)
            return $"Node `{nodeId}` not found in the graph. Run query_codebase to find the correct ID.";

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Edit context summary for `{ctx.Node.Name}`: " +
                   $"{ctx.Callers.Count} direct callers, {ctx.Callees.Count} direct callees, {ctx.Interfaces.Count} interfaces. " +
                   $"File: `{ctx.Node.FilePath ?? "—"}`.";

        var sb = new StringBuilder();
        var sizeHint = ctx.Node.LineCount.HasValue ? $" ({ctx.Node.LineCount} lines)" : string.Empty;
        sb.AppendLine($"## Edit Context — `{ctx.Node.Name}`");
        sb.AppendLine($"**Type:** {ctx.Node.Type} | **File:** `{ctx.Node.FilePath ?? "—"}`{(ctx.Node.LineNumber.HasValue ? $":{ctx.Node.LineNumber}" : "")}{sizeHint}\n");

        if (ctx.Callers.Count > 0)
        {
            sb.AppendLine($"### Callers ({ctx.Callers.Count}) — will be affected by changes");
            foreach (var caller in ctx.Callers)
            {
                var loc = caller.FilePath is not null
                    ? $"`{caller.FilePath}`{(caller.LineNumber.HasValue ? $":{caller.LineNumber}" : "")}"
                    : "—";
                sb.AppendLine($"- **{caller.Type}** `{caller.Name}` — {loc}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Callers — none (safe to change signature)\n");
        }

        if (ctx.Callees.Count > 0)
        {
            sb.AppendLine($"### Calls ({ctx.Callees.Count}) — dependencies this node relies on");
            foreach (var callee in ctx.Callees)
            {
                var loc = callee.FilePath is not null
                    ? $"`{callee.FilePath}`{(callee.LineNumber.HasValue ? $":{callee.LineNumber}" : "")}"
                    : "—";
                sb.AppendLine($"- **{callee.Type}** `{callee.Name}` — {loc}");
            }
            sb.AppendLine();
        }

        if (ctx.Interfaces.Count > 0)
        {
            sb.AppendLine("### Implements");
            foreach (var iface in ctx.Interfaces)
                sb.AppendLine($"- `{iface.Name}`");
            sb.AppendLine();
        }

        sb.AppendLine("> Use find_impact for full transitive blast-radius analysis.");

        return sb.ToString();
    }

    private sealed record ShieldEntry(
        CodeNode TestNode,
        string Reason,
        CodeNode ProtectedNode,
        string MatchType,
        bool IsExactCallerPath,
        int SharedDependencyCount,
        bool SharesLocation);

    private ShieldEntry CreateShieldEntry(
        CodeNode testNode,
        CodeNode protectedNode,
        string matchType,
        ISet<string> targetDependencyKeys,
        ISet<string> protectedDependencyKeys,
        bool isExactCallerPath)
    {
        var sharedDependencyCount = targetDependencyKeys.Intersect(protectedDependencyKeys, StringComparer.OrdinalIgnoreCase).Count();
        var sharesLocation = SameFile(testNode, protectedNode) || SameNamespace(testNode, protectedNode) || FileNameLooksRelated(testNode, protectedNode);
        var reasonParts = new List<string>();

        if (matchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            reasonParts.Add($"directly protects `{protectedNode.Name}`");
        else
            reasonParts.Add($"heuristic match for `{protectedNode.Name}`");

        if (isExactCallerPath)
            reasonParts.Add("exact caller-path seam");

        if (sharedDependencyCount > 0)
            reasonParts.Add(sharedDependencyCount == 1
                ? "shares 1 dependency/contract signal with the target slice"
                : $"shares {sharedDependencyCount} dependency/contract signals with the target slice");

        if (sharesLocation)
            reasonParts.Add("same file/namespace test locality");

        return new ShieldEntry(
            testNode,
            string.Join("; ", reasonParts),
            protectedNode,
            matchType,
            isExactCallerPath,
            sharedDependencyCount,
            sharesLocation);
    }

    private static ISet<string> BuildDependencyKeys(IEnumerable<CodeNode> nodes) =>
        nodes.Select(node =>
            !string.IsNullOrWhiteSpace(node.Id) ? node.Id :
            !string.IsNullOrWhiteSpace(node.Name) ? node.Name :
            node.FilePath)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private bool IsPrimaryShieldCandidate(ShieldEntry entry) =>
        entry.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase) &&
        (entry.IsExactCallerPath || entry.SharedDependencyCount > 0 || entry.SharesLocation);

    private static int ScoreShieldEntry(ShieldEntry entry)
    {
        var score = 0;
        if (entry.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            score += 100;
        if (entry.IsExactCallerPath)
            score += 50;
        score += entry.SharedDependencyCount * 10;
        if (entry.SharesLocation)
            score += 5;

        return score;
    }

    private static string? BuildSuggestedTestCommand(IEnumerable<CodeNode> directShield, IEnumerable<ShieldEntry> primaryShield)
    {
        var candidates = directShield
            .Concat(primaryShield.Select(entry => entry.TestNode))
            .DistinctBy(node => node.Id)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        if (candidates.Length == 1)
            return $"dotnet test --filter FullyQualifiedName~{SanitizeFilterValue(candidates[0].Name)}";

        var directory = candidates
            .Select(node => Path.GetDirectoryName(node.FilePath ?? string.Empty))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (directory.Length == 1)
            return $"dotnet test --filter FullyQualifiedName~{SanitizeFilterValue(Path.GetFileName(directory[0]) ?? "Tests")}";

        return null;
    }

    private static string SanitizeFilterValue(string value)
    {
        var sanitized = Regex.Replace(value, "[^A-Za-z0-9_.]+", string.Empty);
        return string.IsNullOrWhiteSpace(sanitized) ? "Tests" : sanitized;
    }

    public async Task<string> BuildMinimalContextAsync(
        string target,
        string? goal = null,
        int maxTokens = 3000,
        bool includeTests = true,
        bool includeExternalConcepts = true,
        bool includeSourceSnippets = false,
        bool explainPaths = false,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var ctx = await codeGraph.GetContextForEditingAsync(target, cancellationToken);
        var degradations = new List<ContextPackDegradation>();

        if (ctx.Node is null)
            return $"Target `{target}` not found in the graph. Run query_codebase first to find the correct node ID.";

        var impact = await TryContextStepAsync(
            "impact_analysis",
            () => codeGraph.FindImpactAsync(ctx.Node.Id, depth: 2, cancellationToken),
            Array.Empty<(CodeNode Node, int Distance)>(),
            degradations);
        var downstream = await TryContextStepAsync(
            "downstream_traversal",
            () => codeGraph.FindDownstreamAsync(ctx.Node.Id, depth: 2, cancellationToken),
            Array.Empty<(CodeNode Node, int Distance)>(),
            degradations);
        var coverageGaps = includeTests
            ? await TryContextStepAsync(
                "coverage_gap_lookup",
                () => codeGraph.FindCoverageGapsAsync(ctx.Node.ProjectContext, cancellationToken),
                Array.Empty<CodeNode>(),
                degradations)
            : [];
        var relatedTests = includeTests
            ? await TryContextStepAsync(
                "related_test_ranking",
                () => codeGraph.FindRelatedTestsAsync(ctx.Node.Id, ctx.Node.ProjectContext, cancellationToken),
                Array.Empty<(CodeNode Node, string MatchType)>(),
                degradations)
            : [];

        var relatedCoverageGaps = coverageGaps
            .Where(n => SameFile(n, ctx.Node) || SameNamespace(n, ctx.Node) || n.Id == ctx.Node.Id)
            .Where(node => AllowsProfile(node, AnalysisProfile.CoverageGaps))
            .Take(10)
            .ToArray();
        var directRelatedTests = relatedTests
            .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield))
            .ToArray();
        var heuristicRelatedTests = relatedTests
            .Where(match => !match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield))
            .Where(node => SameFile(node, ctx.Node) || SameNamespace(node, ctx.Node) || FileNameLooksRelated(node, ctx.Node) || NameLooksRelated(node.Name, ctx.Node.Name))
            .Take(10)
            .ToArray();

        var candidateFiles = new[] { ctx.Node }
            .Concat(ctx.Callers)
            .Concat(ctx.Callees)
            .Concat(ctx.Interfaces)
            .Concat(impact.Select(i => i.Node))
            .Concat(downstream.Select(d => d.Node))
            .Concat(directRelatedTests)
            .Concat(heuristicRelatedTests)
            .Where(node => AllowsProfile(node, AnalysisProfile.AgentContext))
            .Select(n => n.FilePath)
            .OfType<string>()
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        ContextCost contextCost;
        try
        {
            contextCost = EstimateContextCost(
                ctx,
                impact,
                downstream,
                relatedCoverageGaps,
                directRelatedTests.Length + heuristicRelatedTests.Length,
                candidateFiles.Length,
                includeSourceSnippets);
        }
        catch (Exception ex)
        {
            degradations.Add(new ContextPackDegradation("token_budgeting", ex));
            contextCost = EstimateFallbackContextCost(ctx, impact, downstream, includeSourceSnippets);
        }
        var filteredCallers = FilterNodesByProfile(ctx.Callers, AnalysisProfile.AgentContext);
        var filteredCallees = FilterNodesByProfile(ctx.Callees, AnalysisProfile.AgentContext);
        var filteredInterfaces = FilterNodesByProfile(ctx.Interfaces, AnalysisProfile.AgentContext);
        var filteredImpact = FilterNodePairsByProfile(impact, AnalysisProfile.AgentContext);
        var filteredDownstream = FilterNodePairsByProfile(downstream, AnalysisProfile.AgentContext);
        var sb = new StringBuilder();
        var snippetBudget = includeSourceSnippets
            ? Math.Max(0, maxTokens - contextCost.EstimatedTokens + contextCost.SourceSnippetTokens)
            : 0;
        IReadOnlyList<SourceSnippet> snippets;
        if (!includeSourceSnippets)
        {
            snippets = [];
        }
        else
        {
            try
            {
                snippets = BuildSourceSnippets(
                    new[] { ctx.Node }
                        .Concat(ctx.Callees)
                        .Concat(ctx.Interfaces)
                        .Concat(directRelatedTests)
                        .Where(node => AllowsProfile(node, AnalysisProfile.AgentContext))
                        .DistinctBy(n => n.Id)
                        .Take(detailLevel == ContextDetailLevel.Full ? 5 : 3),
                    snippetBudget,
                    detailLevel);
            }
            catch (Exception ex)
            {
                degradations.Add(new ContextPackDegradation("source_snippet_budgeting", ex));
                snippets = [];
            }
        }

        sb.AppendLine($"## Minimal Context Pack — `{ctx.Node.Name}`");
        if (!string.IsNullOrWhiteSpace(goal))
            sb.AppendLine($"**Goal:** {goal}");
        sb.AppendLine($"**Budget:** {maxTokens} tokens | **Estimated:** {contextCost.EstimatedTokens:N0} tokens | **Detail:** {detailLevel}");
        sb.AppendLine($"**Complexity:** {contextCost.Complexity} | **Model guidance:** {contextCost.ModelGuidance}");
        sb.AppendLine($"**Expansion risk:** {contextCost.ExpansionRisk} — {contextCost.Reason}");
        sb.AppendLine($"**Target:** {ctx.Node.Type} `{ctx.Node.Name}`");
        sb.AppendLine($"**File:** `{ctx.Node.FilePath ?? "—"}`{(ctx.Node.LineNumber.HasValue ? $":{ctx.Node.LineNumber}" : "")}{(ctx.Node.LineCount.HasValue ? $" ({ctx.Node.LineCount} lines)" : "")}");
        if (!string.IsNullOrWhiteSpace(ctx.Node.Summary))
            sb.AppendLine($"**Summary:** {ctx.Node.Summary}");
        sb.AppendLine();

        AppendNodeList(sb, "Direct callers", filteredCallers, detailLevel, "who will be affected by signature or behavior changes");
        AppendNodeList(sb, "Direct callees", filteredCallees, detailLevel, "dependencies this target relies on");
        AppendNodeList(sb, "Interfaces", filteredInterfaces, detailLevel, "contracts related to this target");
        AppendDistanceList(sb, "Near impact", filteredImpact, detailLevel, "transitive callers within 2 hops");
        AppendDistanceList(sb, "Near downstream", filteredDownstream, detailLevel, "dependencies within 2 hops");

        if (includeTests)
        {
            AppendNodeList(sb, "Relevant coverage gaps", relatedCoverageGaps, detailLevel, "heuristic matches by same file/namespace/target");
            AppendRelatedTestsList(sb, directRelatedTests, heuristicRelatedTests, detailLevel);
        }

        if (candidateFiles.Length > 0)
        {
            if (explainPaths)
            {
                try
                {
                    var explainedFiles = await BuildExplainedFilesAsync(
                        ctx.Node,
                        filteredCallers,
                        filteredCallees,
                        filteredInterfaces,
                        filteredImpact,
                        filteredDownstream,
                        directRelatedTests,
                        heuristicRelatedTests,
                        relatedCoverageGaps,
                        cancellationToken);

                    AppendExplainedFiles(sb, explainedFiles);
                }
                catch (Exception ex)
                {
                    AppendCandidateFiles(sb, candidateFiles);
                    degradations.Add(new ContextPackDegradation("file_path_explanation", ex));
                }
            }
            else
            {
                AppendCandidateFiles(sb, candidateFiles);
            }
        }

        if (includeSourceSnippets)
            AppendSourceSnippets(sb, snippets, snippetBudget);

        if (includeExternalConcepts)
            sb.AppendLine("> External concepts are included when present in callers/callees/impact/downstream graph results.");

        AppendDegradedContextSection(sb, degradations);
        sb.AppendLine($"> Token estimate is approximate. {(contextCost.EstimatedTokens > maxTokens ? "Consider Summary detail, fewer optional sections, or a larger context budget." : "Current pack fits the requested budget.")}");

        return sb.ToString();
    }

    private static async Task<T> TryContextStepAsync<T>(
        string step,
        Func<Task<T>> action,
        T fallback,
        ICollection<ContextPackDegradation> degradations)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            degradations.Add(new ContextPackDegradation(step, ex));
            return fallback;
        }
    }

    private static void AppendCandidateFiles(StringBuilder sb, IReadOnlyCollection<string> candidateFiles)
    {
        sb.AppendLine("### Files likely needed");
        foreach (var file in candidateFiles)
            sb.AppendLine($"- `{file}`");
        sb.AppendLine();
    }

    private static void AppendDegradedContextSection(StringBuilder sb, IReadOnlyCollection<ContextPackDegradation> degradations)
    {
        if (degradations.Count == 0)
            return;

        sb.AppendLine("### Degraded mode");
        sb.AppendLine("`context_pack_status=degraded`");
        foreach (var degradation in degradations)
        {
            sb.AppendLine($"- failed_step: `{degradation.Step}`");
            sb.AppendLine($"- exception: `{degradation.ExceptionType}`");
        }
        sb.AppendLine("- fallback: use `resolve_exact_symbol`, `find_impact`, and `find_test_shield` for exact target, blast radius, and test coverage when one or more context-pack sub-steps fail.");
        sb.AppendLine();
    }

    private static ContextCost EstimateFallbackContextCost(
        EditingContext ctx,
        IReadOnlyCollection<(CodeNode Node, int Distance)> impact,
        IReadOnlyCollection<(CodeNode Node, int Distance)> downstream,
        bool includeSourceSnippets)
    {
        var nodeCount = 1 + ctx.Callers.Count + ctx.Callees.Count + ctx.Interfaces.Count + impact.Count + downstream.Count;
        var sourceSnippetTokens = includeSourceSnippets ? 300 : 0;
        var estimatedTokens = 400 + (nodeCount * 60) + sourceSnippetTokens;
        var complexity = nodeCount > 20 ? "High" : nodeCount > 8 ? "Medium" : "Low";
        var expansionRisk = nodeCount > 20 ? "High" : nodeCount > 8 ? "Medium" : "Low";
        var modelGuidance = nodeCount > 20
            ? "Use a larger reasoning model or larger context window because fallback cost estimation detected a broad graph slice."
            : nodeCount > 8
                ? "Use a medium-capability model because fallback cost estimation detected a moderate graph slice."
                : "Small or fast model likely sufficient for this fallback-sized context pack.";
        var reason = "Fallback estimate derived from the available target, edit context, and surviving graph slices after degraded-mode recovery.";

        return new ContextCost(estimatedTokens, sourceSnippetTokens, complexity, modelGuidance, expansionRisk, reason);
    }

    private sealed record ContextPackDegradation(string Step, string ExceptionType)
    {
        public ContextPackDegradation(string step, Exception exception)
            : this(step, exception.GetType().Name)
        {
        }
    }
}
