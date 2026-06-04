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

        var estimatedTokens = EstimateContextTokens(
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
        sb.AppendLine($"**Budget:** {maxTokens} tokens | **Estimated:** {estimatedTokens} tokens | **Detail:** {detailLevel}");
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

        sb.AppendLine($"> Token estimate is approximate. {(estimatedTokens > maxTokens ? "Consider Summary detail or a larger context budget." : "Current pack fits the requested budget.")}");

        return sb.ToString();
    }

    public async Task<string> FindGodClassesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3, cancellationToken);

        if (results.Count == 0)
            return $"No god classes found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Either all classes are well-sized, or line count data is missing — re-index to populate it.";

        var sb = new StringBuilder();
        sb.AppendLine($"## God Classes{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Classes that are **large** (SRP violation) **and** heavily depended upon (high fan-in):\n");
        sb.AppendLine("| Risk | Lines | Fan-in | Name | File |");
        sb.AppendLine("|------|-------|--------|------|------|");

        foreach (var (node, lineCount, fanIn) in results)
        {
            var risk = (fanIn * 10 + lineCount) switch
            {
                > 500 => "Critical",
                > 200 => "High",
                _ => "Medium"
            };
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {risk} | {lineCount} | {fanIn} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Use get_context_for_editing before refactoring — high fan-in means all callers need updating too.");

        return sb.ToString();
    }

    public async Task<string> FindStaleKnowledgeAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var documents = await vectorStore.ListAsync(projectContext, limit: 250, cancellationToken);
        var externalConcepts = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                TypeFilter = CodeNodeType.ExternalConcept,
                Limit = 200
            },
            cancellationToken);
        var unreferenced = await codeGraph.FindUnreferencedAsync(projectContext, cancellationToken);
        var mostRecentCodeUpdate = await codeGraph.GetMostRecentCodeUpdateAsync(projectContext, cancellationToken);

        var unresolvedDocMentions = new List<(KnowledgeDocument Document, string Mention, string Reason)>();
        var staleNotes = new List<(KnowledgeDocument Document, string Reason)>();
        var orphanedConcepts = new List<CodeNode>();

        foreach (var doc in documents)
        {
            var explicitMentions = ExtractMentionIds(doc.Metadata);
            if (explicitMentions.Count > 0)
            {
                foreach (var mention in explicitMentions)
                {
                    var ctx = await codeGraph.GetContextForEditingAsync(mention, cancellationToken);
                    if (ctx.Node is null)
                    {
                        unresolvedDocMentions.Add((doc, mention, "explicit metadata reference does not resolve to a current code node"));
                        continue;
                    }

                    if (mostRecentCodeUpdate.HasValue
                        && doc.UpdatedAt.HasValue
                        && doc.UpdatedAt.Value < mostRecentCodeUpdate.Value
                        && IsLikelyNote(doc))
                    {
                        staleNotes.Add((doc, "note is older than the latest code reindex"));
                    }
                }

                continue;
            }

            foreach (var mention in ExtractSymbolMentions(doc.Content).Take(8))
            {
                var matches = await codeGraph.QueryNodesAsync(
                    new CodeGraphQuery
                    {
                        ProjectContext = doc.ProjectContext ?? projectContext,
                        SemanticQuery = mention,
                        Limit = 5
                    },
                    cancellationToken);

                if (matches.Count == 0 || !matches.Any(IsLikelyMatch))
                {
                    unresolvedDocMentions.Add((doc, mention, "documentation mentions code that no longer resolves cleanly"));
                }
            }

            if (mostRecentCodeUpdate.HasValue
                && doc.UpdatedAt.HasValue
                && doc.UpdatedAt.Value < mostRecentCodeUpdate.Value
                && IsLikelyNote(doc))
            {
                staleNotes.Add((doc, "note is older than the latest code reindex"));
            }
        }

        foreach (var concept in externalConcepts)
        {
            var edges = await codeGraph.QueryEdgesAsync(concept.Id, depth: 1, cancellationToken);
            if (edges.Count == 0)
                orphanedConcepts.Add(concept);
        }

        var staleOrphans = unreferenced
            .Where(node => node.Type is CodeNodeType.Method or CodeNodeType.Class)
            .Take(limit)
            .ToArray();

        if (unresolvedDocMentions.Count == 0 && staleNotes.Count == 0 && orphanedConcepts.Count == 0 && staleOrphans.Length < 10)
        {
            return $"No obvious stale knowledge found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Knowledge docs, external concepts, and code graph references appear consistent.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Stale Knowledge{(projectContext is not null ? $" â€” {projectContext}" : "")}");
        sb.AppendLine("Possibly stale knowledge found in documents, external concepts, and orphaned code references:\n");

        if (unresolvedDocMentions.Count > 0)
        {
            sb.AppendLine($"### Unresolved documentation references ({Math.Min(unresolvedDocMentions.Count, limit)})");
            foreach (var finding in unresolvedDocMentions.Take(limit))
            {
                sb.AppendLine($"- `{finding.Document.Source ?? finding.Document.Id}` mentions `{finding.Mention}` â€” {finding.Reason}");
            }
            sb.AppendLine();
        }

        if (orphanedConcepts.Count > 0)
        {
            sb.AppendLine($"### Orphaned external concepts ({Math.Min(orphanedConcepts.Count, limit)})");
            foreach (var concept in orphanedConcepts.Take(limit))
            {
                sb.AppendLine($"- `{concept.Name}` (`{concept.Id}`) has no live code links");
            }
            sb.AppendLine();
        }

        if (staleNotes.Count > 0)
        {
            sb.AppendLine($"### Old notes ({Math.Min(staleNotes.Count, limit)})");
            foreach (var finding in staleNotes.Take(limit))
            {
                var source = finding.Document.Source ?? finding.Document.Id;
                sb.AppendLine($"- `{source}` â€” {finding.Reason}");
            }
            sb.AppendLine();
        }

        if (staleOrphans.Length >= 10)
        {
            sb.AppendLine($"### Orphaned code nodes ({staleOrphans.Length})");
            foreach (var node in staleOrphans.Take(limit))
                sb.AppendLine($"- `{node.Name}` â€” `{node.FilePath ?? "â€”"}`");
            sb.AppendLine();
        }

        sb.AppendLine("> Weak `Mentions` / `References` relationships from knowledge to code are preferred for explicit links. " +
                      "When those are absent, CodeMeridian falls back to text-based detection and reports likely stale references instead of silently rewriting them.");

        return sb.ToString();
    }

    public async Task<string> FindDownstreamAsync(
        string nodeId,
        int depth = 5,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindDownstreamAsync(nodeId, depth, cancellationToken);

        if (results.Count == 0)
            return $"No downstream dependencies found for `{nodeId}` within {depth} hops. " +
                   "The node may not exist or has no outbound Calls/Uses/DependsOn edges.";

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Downstream summary for `{nodeId}`: {results.Count} dependencies within {depth} hops. " +
                   $"Nearest distance: {results.Min(r => r.Distance)}. Farthest distance: {results.Max(r => r.Distance)}.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Downstream Blast Radius — `{nodeId}`");
        sb.AppendLine($"**{results.Count}** elements that this node transitively calls or depends on (up to {depth} hops):\n");
        sb.AppendLine("| Distance | Type | Name | File |");
        sb.AppendLine("|----------|------|------|------|");

        foreach (var (node, dist) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {dist} | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Combined with find_impact (backward), this gives the full change surface.");

        return sb.ToString();
    }

    public async Task<string> FindCyclesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindCyclesAsync(projectContext, cancellationToken);

        if (results.Count == 0)
            return $"No namespace-level circular dependencies found" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Clean architecture — no bidirectional namespace coupling detected.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Circular Dependencies{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** namespace pairs have bidirectional dependencies (A→B AND B→A):\n");
        sb.AppendLine("| Namespace A | ↔ | Namespace B |");
        sb.AppendLine("|------------|---|------------|");

        foreach (var (nsA, nsB) in results)
            sb.AppendLine($"| `{nsA}` | ↔ | `{nsB}` |");

        sb.AppendLine();
        sb.AppendLine("> Circular dependencies prevent clean layering. Introduce an abstraction (interface) to break the cycle.");

        return sb.ToString();
    }

    public async Task<string> FindArchitectureViolationsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindArchitectureViolationsAsync(projectContext, cancellationToken);

        if (results.Count == 0)
            return $"No Clean Architecture violations found" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Core and Application layers have no illegal outbound dependencies.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Architecture Violations{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** edges break Clean Architecture layer rules:\n");
        sb.AppendLine("| Violation Rule | Source | Target | Source File |");
        sb.AppendLine("|---------------|--------|--------|-------------|");

        foreach (var (source, target, violation) in results)
        {
            var file = source.FilePath is not null ? $"`{source.FilePath}`" : "—";
            sb.AppendLine($"| {violation} | `{source.Name}` | `{target.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Rules: Core must not depend on Application/Infrastructure/McpServer. " +
                      "Application must not depend on Infrastructure/McpServer.");

        return sb.ToString();
    }

    public async Task<string> FindHighChurnAsync(
        string? projectContext = null,
        int threshold = 3,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindHighChurnAsync(projectContext, threshold, cancellationToken);

        if (results.Count == 0)
            return $"No high-churn nodes found (threshold: {threshold} re-indexes)" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Either the codebase is stable or nodes haven't been indexed multiple times yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"## High-Churn Nodes{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** nodes re-indexed ≥{threshold} times (frequently changed):\n");
        sb.AppendLine("| Churn | Type | Name | File |");
        sb.AppendLine("|-------|------|------|------|");

        foreach (var (node, changeCount) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {changeCount}× | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> High churn + high fan-in = maximum technical debt risk. Cross-reference with find_hotspots.");

        return sb.ToString();
    }

    private static bool IsLikelyNote(KnowledgeDocument document)
    {
        var source = document.Source ?? string.Empty;
        var kind = GetMetadataValue(document.Metadata, "kind");

        return source.Contains("adr", StringComparison.OrdinalIgnoreCase)
               || source.Contains("note", StringComparison.OrdinalIgnoreCase)
               || source.Contains("decision", StringComparison.OrdinalIgnoreCase)
               || source.Contains("agent", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("agent-note", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("note", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? value : string.Empty;

    private static List<string> ExtractMentionIds(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "relatedNodeIds", "relatedNodes", "mentions" })
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            return raw
                .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static IEnumerable<string> ExtractSymbolMentions(string content)
    {
        var dotted = DottedSymbolRegex().Matches(content)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var memberLike = MemberLikeRegex().Matches(content)
            .Select(match => match.Value)
            .Where(value => value.Length >= 6)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return dotted.Concat(memberLike);
    }

    private static bool IsLikelyMatch(CodeNode node) =>
        node.Type is CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.Method or CodeNodeType.ExternalConcept;

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\b", RegexOptions.Compiled)]
    private static partial Regex DottedSymbolRegex();

    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9_]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex MemberLikeRegex();

    private static TimeSpan ParseWindow(string window) => window.ToLowerInvariant() switch
    {
        var w when w.EndsWith('h') && int.TryParse(w[..^1], out var h) => TimeSpan.FromHours(h),
        var w when w.EndsWith('d') && int.TryParse(w[..^1], out var d) => TimeSpan.FromDays(d),
        var w when w.EndsWith('m') && int.TryParse(w[..^1], out var m) => TimeSpan.FromMinutes(m),
        _ => TimeSpan.FromHours(24)
    };

    private static void AppendNodeList(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<CodeNode> nodes,
        ContextDetailLevel detailLevel,
        string note)
    {
        sb.AppendLine($"### {title} ({nodes.Count})");
        if (nodes.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        if (detailLevel == ContextDetailLevel.Summary)
        {
            sb.AppendLine($"- {nodes.Count} nodes ({note})");
            sb.AppendLine();
            return;
        }

        foreach (var node in nodes.Take(detailLevel == ContextDetailLevel.Full ? 50 : 10))
        {
            var loc = node.FilePath is not null
                ? $" — `{node.FilePath}`{(node.LineNumber.HasValue ? $":{node.LineNumber}" : "")}"
                : string.Empty;
            sb.AppendLine($"- **{node.Type}** `{node.Name}`{loc}");
        }

        if (detailLevel != ContextDetailLevel.Full && nodes.Count > 10)
            sb.AppendLine($"- ...{nodes.Count - 10} more");

        sb.AppendLine();
    }

    private static void AppendDistanceList(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<(CodeNode Node, int Distance)> nodes,
        ContextDetailLevel detailLevel,
        string note)
    {
        sb.AppendLine($"### {title} ({nodes.Count})");
        if (nodes.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        if (detailLevel == ContextDetailLevel.Summary)
        {
            sb.AppendLine($"- {nodes.Count} nodes ({note})");
            sb.AppendLine();
            return;
        }

        foreach (var (node, distance) in nodes.Take(detailLevel == ContextDetailLevel.Full ? 50 : 10))
        {
            var loc = node.FilePath is not null ? $" — `{node.FilePath}`" : string.Empty;
            sb.AppendLine($"- d{distance}: **{node.Type}** `{node.Name}`{loc}");
        }

        if (detailLevel != ContextDetailLevel.Full && nodes.Count > 10)
            sb.AppendLine($"- ...{nodes.Count - 10} more");

        sb.AppendLine();
    }

    private static void AppendRelatedTestsList(
        StringBuilder sb,
        IReadOnlyCollection<CodeNode> directTests,
        IReadOnlyCollection<CodeNode> heuristicTests,
        ContextDetailLevel detailLevel)
    {
        var total = directTests.Count + heuristicTests.Count;
        sb.AppendLine($"### Relevant tests ({total})");

        if (total == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        if (detailLevel == ContextDetailLevel.Summary)
        {
            sb.AppendLine($"- {directTests.Count} direct test callers, {heuristicTests.Count} heuristic matches");
            sb.AppendLine();
            return;
        }

        if (directTests.Count > 0)
        {
            sb.AppendLine("Direct test callers:");
            foreach (var node in directTests)
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{FormatLocation(node)}");
        }

        if (heuristicTests.Count > 0)
        {
            sb.AppendLine("Heuristic matches:");
            foreach (var node in heuristicTests)
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{FormatLocation(node)} (heuristic)");
        }

        sb.AppendLine();
    }

    private static string FormatLocation(CodeNode node) =>
        node.FilePath is not null
            ? $" — `{node.FilePath}`{(node.LineNumber.HasValue ? $":{node.LineNumber}" : "")}"
            : string.Empty;

    private static bool SameFile(CodeNode left, CodeNode right) =>
        !string.IsNullOrWhiteSpace(left.FilePath)
        && string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);

    private static bool SameNamespace(CodeNode left, CodeNode right) =>
        !string.IsNullOrWhiteSpace(left.Namespace)
        && string.Equals(left.Namespace, right.Namespace, StringComparison.OrdinalIgnoreCase);

    private static bool FileNameLooksRelated(CodeNode left, CodeNode right)
    {
        if (string.IsNullOrWhiteSpace(left.FilePath) || string.IsNullOrWhiteSpace(right.FilePath))
            return false;

        var leftName = Path.GetFileNameWithoutExtension(left.FilePath);
        var rightName = Path.GetFileNameWithoutExtension(right.FilePath);

        return NameLooksRelated(leftName, rightName)
            || NameLooksRelated(leftName, right.Name)
            || NameLooksRelated(rightName, left.Name);
    }

    private static bool NameLooksRelated(string left, string right) =>
        left.Contains(right, StringComparison.OrdinalIgnoreCase)
        || right.Contains(left, StringComparison.OrdinalIgnoreCase);

    private static int EstimateContextTokens(
        EditingContext ctx,
        IReadOnlyCollection<(CodeNode Node, int Distance)> impact,
        IReadOnlyCollection<(CodeNode Node, int Distance)> downstream,
        IReadOnlyCollection<CodeNode> coverageGaps,
        int relatedTestCount,
        int fileCount,
        bool includeSourceSnippets)
    {
        var nodeRows = 1 + ctx.Callers.Count + ctx.Callees.Count + ctx.Interfaces.Count + impact.Count + downstream.Count + coverageGaps.Count + relatedTestCount;
        var estimate = 120
            + nodeRows * 22
            + fileCount * 16
            + (string.IsNullOrWhiteSpace(ctx.Node?.Summary) ? 0 : 80);

        if (includeSourceSnippets)
            estimate += Math.Min(ctx.Node?.LineCount ?? 20, 80) * 12;

        return estimate;
    }
}
