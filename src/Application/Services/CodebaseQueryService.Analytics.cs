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
        CancellationToken cancellationToken = default)
    {
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
        foreach (var (node, fanIn) in results)
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

        if (results.Count == 0)
            return $"No coverage gaps found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "All production classes and methods appear to be called from test namespaces.";

        var grouped = results.GroupBy(n => n.Type).OrderBy(g => g.Key.ToString()).ToArray();

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Coverage gap summary{(projectContext is not null ? $" for '{projectContext}'" : "")}: " +
                   string.Join(", ", grouped.Select(g => $"{g.Count()} {g.Key}"));

        var sb = new StringBuilder();
        sb.AppendLine($"## Test Coverage Gaps{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** production types/methods with no test calling them:\n");

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

        if (results.Count == 0)
            return $"No large classes (>{classThreshold} lines) or methods (>{methodThreshold} lines) found" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Re-index with an up-to-date indexer to populate line counts.";

        var grouped = results.GroupBy(n => n.Type).OrderBy(g => g.Key.ToString()).ToArray();

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Large node summary{(projectContext is not null ? $" for '{projectContext}'" : "")}: " +
                   string.Join(", ", grouped.Select(g => $"{g.Count()} {g.Key}")) +
                   $". Largest element: `{results.OrderByDescending(n => n.LineCount).First().Name}`.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Large Node Analysis{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** oversized elements (classes >{classThreshold} lines, methods >{methodThreshold} lines):\n");

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

    public async Task<string> BuildMinimalContextAsync(
        string target,
        string? goal = null,
        int maxTokens = 3000,
        bool includeTests = true,
        bool includeExternalConcepts = true,
        bool includeSourceSnippets = false,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var ctx = await codeGraph.GetContextForEditingAsync(target, cancellationToken);

        if (ctx.Node is null)
            return $"Target `{target}` not found in the graph. Run query_codebase first to find the correct node ID.";

        var impact = await codeGraph.FindImpactAsync(ctx.Node.Id, depth: 2, cancellationToken);
        var downstream = await codeGraph.FindDownstreamAsync(ctx.Node.Id, depth: 2, cancellationToken);
        var coverageGaps = includeTests
            ? await codeGraph.FindCoverageGapsAsync(ctx.Node.ProjectContext, cancellationToken)
            : [];
        var relatedTests = includeTests
            ? await codeGraph.FindRelatedTestsAsync(ctx.Node.Id, ctx.Node.ProjectContext, cancellationToken)
            : [];

        var relatedCoverageGaps = coverageGaps
            .Where(n => SameFile(n, ctx.Node) || SameNamespace(n, ctx.Node) || n.Id == ctx.Node.Id)
            .Take(10)
            .ToArray();
        var directRelatedTests = relatedTests
            .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .ToArray();
        var heuristicRelatedTests = relatedTests
            .Where(match => !match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
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
            .Select(n => n.FilePath)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        var contextCost = EstimateContextCost(
            ctx,
            impact,
            downstream,
            relatedCoverageGaps,
            directRelatedTests.Length + heuristicRelatedTests.Length,
            candidateFiles.Length,
            includeSourceSnippets);
        var sb = new StringBuilder();

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

        AppendNodeList(sb, "Direct callers", ctx.Callers, detailLevel, "who will be affected by signature or behavior changes");
        AppendNodeList(sb, "Direct callees", ctx.Callees, detailLevel, "dependencies this target relies on");
        AppendNodeList(sb, "Interfaces", ctx.Interfaces, detailLevel, "contracts related to this target");
        AppendDistanceList(sb, "Near impact", impact, detailLevel, "transitive callers within 2 hops");
        AppendDistanceList(sb, "Near downstream", downstream, detailLevel, "dependencies within 2 hops");

        if (includeTests)
        {
            AppendNodeList(sb, "Relevant coverage gaps", relatedCoverageGaps, detailLevel, "heuristic matches by same file/namespace/target");
            AppendRelatedTestsList(sb, directRelatedTests, heuristicRelatedTests, detailLevel);
        }

        if (candidateFiles.Length > 0)
        {
            sb.AppendLine("### Files likely needed");
            foreach (var file in candidateFiles)
                sb.AppendLine($"- `{file}`");
            sb.AppendLine();
        }

        if (includeExternalConcepts)
            sb.AppendLine("> External concepts are included when present in callers/callees/impact/downstream graph results.");

        if (includeSourceSnippets)
            sb.AppendLine("> Source snippets requested, but source extraction is not implemented yet. Use the listed files and target line number.");

        sb.AppendLine($"> Token estimate is approximate. {(contextCost.EstimatedTokens > maxTokens ? "Consider Summary detail, fewer optional sections, or a larger context budget." : "Current pack fits the requested budget.")}");

        return sb.ToString();
    }
}
