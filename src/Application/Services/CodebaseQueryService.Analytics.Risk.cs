using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> FindGodClassesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3, cancellationToken);
        var filteredResults = results.Where(item => AllowsProfile(item.Node, AnalysisProfile.DesignSmells)).ToArray();

        if (filteredResults.Length == 0)
            return $"No god classes found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Either all classes are well-sized, or line count data is missing — re-index to populate it.";

        var sb = new StringBuilder();
        sb.AppendLine($"## God Classes{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Classes that are **large** (SRP violation) **and** heavily depended upon (high fan-in):\n");
        sb.AppendLine("| Risk | Lines | Fan-in | Name | File |");
        sb.AppendLine("|------|-------|--------|------|------|");

        foreach (var (node, lineCount, fanIn) in filteredResults)
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

            if (!ShouldScanHeuristicMentions(doc))
                continue;

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
        sb.AppendLine($"**{results.Count}** nodes re-indexed ≥{threshold} times (frequently changed). Production code is ranked ahead of namespaces and tests:\n");
        sb.AppendLine("| Churn | Type | Name | File |");
        sb.AppendLine("|-------|------|------|------|");

        foreach (var (node, changeCount) in results
                     .OrderBy(item => NodeDisplayRank(item.Node))
                     .ThenByDescending(item => item.ChangeCount))
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

    private bool ShouldScanHeuristicMentions(KnowledgeDocument document)
    {
        var source = document.Source ?? string.Empty;
        return !analysisOptions.StaleKnowledge.SkipHeuristicSourcePrefixes
            .Any(prefix => source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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

    private IEnumerable<string> ExtractSymbolMentions(string content)
    {
        var dotted = DottedSymbolRegex().Matches(content)
            .Select(match => match.Value)
            .Where(IsLikelySymbolMention)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var memberLike = MemberLikeRegex().Matches(content)
            .Select(match => match.Value)
            .Where(value => value.Length >= 6)
            .Where(IsLikelySymbolMention)
            .Where(IsLikelySingleSymbolMention)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return dotted.Concat(memberLike);
    }

    private bool IsLikelySingleSymbolMention(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal))
            return true;

        return analysisOptions.StaleKnowledge.CodeLikeSuffixes.Any(suffix => value.EndsWith(suffix, StringComparison.Ordinal));
    }

    private bool IsLikelySymbolMention(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim('`', '\'', '"', '.', ',', ':', ';', '(', ')', '[', ']');
        if (normalized.Length < 4)
            return false;

        if (analysisOptions.StaleKnowledge.IgnoredMentionTokens.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return false;

        if (normalized.Contains('.', StringComparison.Ordinal))
        {
            var lower = normalized.ToLowerInvariant();
            var firstSegment = normalized.Split('.')[0];
            if (firstSegment.Length > 0 && char.IsLower(firstSegment[0]))
                return false;

            if (lower.Contains("example.com", StringComparison.Ordinal)
                || lower.Contains("localhost", StringComparison.Ordinal)
                || lower.StartsWith("www.", StringComparison.Ordinal)
                || analysisOptions.StaleKnowledge.IgnoredDottedSuffixes.Any(suffix => lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
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

    private static ContextCost EstimateContextCost(
        EditingContext ctx,
        IReadOnlyCollection<(CodeNode Node, int Distance)> impact,
        IReadOnlyCollection<(CodeNode Node, int Distance)> downstream,
        IReadOnlyCollection<CodeNode> coverageGaps,
        int relatedTestCount,
        int fileCount,
        bool includeSourceSnippets)
    {
        var target = ctx.Node;
        var relationshipRows = ctx.Callers.Count + ctx.Callees.Count + ctx.Interfaces.Count + impact.Count + downstream.Count;
        var nodeRows = 1 + relationshipRows + coverageGaps.Count + relatedTestCount;
        var summaryTokens = EstimateSummaryTokens(target);
        var sourceSnippetTokens = includeSourceSnippets ? EstimateSourceSnippetTokens(target) : 0;
        var estimate = 120
            + nodeRows * 20
            + relationshipRows * 15
            + fileCount * 16
            + summaryTokens
            + sourceSnippetTokens;

        var affectedNodes = impact.Count;
        var dependencyCount = downstream.Count;
        var missingTests = coverageGaps.Count > 0;
        var churnScore = target?.ChangeCount ?? 0;
        var size = target?.LineCount ?? 0;
        var crossProjectEdges =
            impact.Count(i => !SameProject(target, i.Node))
            + downstream.Count(d => !SameProject(target, d.Node))
            + ctx.Callers.Count(n => !SameProject(target, n))
            + ctx.Callees.Count(n => !SameProject(target, n));
        var riskPoints = 0;

        if (estimate > 12000) riskPoints += 3;
        else if (estimate > 6000) riskPoints += 2;
        else if (estimate > 3000) riskPoints += 1;

        if (affectedNodes >= 25) riskPoints += 3;
        else if (affectedNodes >= 8) riskPoints += 2;
        else if (affectedNodes >= 3) riskPoints += 1;

        if (dependencyCount >= 20) riskPoints += 2;
        else if (dependencyCount >= 8) riskPoints += 1;

        if (crossProjectEdges > 0) riskPoints += Math.Min(crossProjectEdges, 3);
        if (missingTests) riskPoints += 1;
        if (relatedTestCount == 0) riskPoints += 1;
        if (size >= 300) riskPoints += 2;
        else if (size >= 100) riskPoints += 1;
        if (churnScore >= 5) riskPoints += 2;
        else if (churnScore >= 2) riskPoints += 1;

        var complexity = riskPoints >= 7 ? "High"
            : riskPoints >= 3 ? "Medium"
            : "Low";
        var modelGuidance = complexity switch
        {
            "High" => "Use a larger reasoning model or larger context window",
            "Medium" => "Use a standard coding model",
            _ => "Small or fast model likely sufficient"
        };
        var expansionRisk = estimate > 12000 || affectedNodes >= 25 || crossProjectEdges > 2 ? "High"
            : estimate > 3000 || affectedNodes >= 8 || dependencyCount >= 8 || missingTests ? "Medium"
            : "Low";
        var reason = BuildContextCostReason(
            estimate,
            affectedNodes,
            dependencyCount,
            crossProjectEdges,
            coverageGaps.Count,
            relatedTestCount,
            size,
            churnScore);

        return new ContextCost(estimate, sourceSnippetTokens, complexity, modelGuidance, expansionRisk, reason);
    }

    private static int EstimateSummaryTokens(CodeNode? node) =>
        string.IsNullOrWhiteSpace(node?.Summary)
            ? 0
            : node.Type == CodeNodeType.Class ? 150 : 80;

    private static int EstimateSourceSnippetTokens(CodeNode? node)
    {
        var lineCount = Math.Min(node?.LineCount ?? 20, 80);
        return lineCount * 12;
    }

    private static bool SameProject(CodeNode? target, CodeNode related) =>
        string.IsNullOrWhiteSpace(target?.ProjectContext)
        || string.IsNullOrWhiteSpace(related.ProjectContext)
        || string.Equals(target.ProjectContext, related.ProjectContext, StringComparison.OrdinalIgnoreCase);

    private static string BuildContextCostReason(
        int estimatedTokens,
        int affectedNodes,
        int dependencyCount,
        int crossProjectEdges,
        int coverageGapCount,
        int relatedTestCount,
        int lineCount,
        int changeCount)
    {
        var parts = new List<string>
        {
            $"{estimatedTokens:N0} estimated tokens",
            $"{affectedNodes} affected nodes",
            $"{dependencyCount} downstream dependencies"
        };

        if (crossProjectEdges > 0)
            parts.Add($"{crossProjectEdges} cross-project edges");
        if (coverageGapCount > 0)
            parts.Add($"{coverageGapCount} nearby coverage gaps");
        if (relatedTestCount == 0)
            parts.Add("no related tests found");
        else
            parts.Add($"{relatedTestCount} related tests");
        if (lineCount > 0)
            parts.Add($"{lineCount} target lines");
        if (changeCount > 0)
            parts.Add($"{changeCount} indexed changes");

        return string.Join(", ", parts);
    }

    private sealed record ContextCost(
        int EstimatedTokens,
        int SourceSnippetTokens,
        string Complexity,
        string ModelGuidance,
        string ExpansionRisk,
        string Reason);

    private static IReadOnlyList<SourceSnippet> BuildSourceSnippets(
        IEnumerable<CodeNode> nodes,
        int tokenBudget,
        ContextDetailLevel detailLevel)
    {
        if (tokenBudget <= 0)
            return [];

        var remaining = tokenBudget;
        var snippets = new List<SourceSnippet>();
        var maxLinesPerNode = detailLevel == ContextDetailLevel.Full ? 40 : 20;

        foreach (var node in nodes)
        {
            if (remaining < 80)
                break;

            var snippet = TryBuildSourceSnippet(node, remaining, maxLinesPerNode);
            if (snippet is null)
                continue;

            snippets.Add(snippet);
            remaining -= snippet.EstimatedTokens;
        }

        return snippets;
    }

    private static SourceSnippet? TryBuildSourceSnippet(CodeNode node, int tokenBudget, int maxLinesPerNode)
    {
        if (string.IsNullOrWhiteSpace(node.SourceSnippet) || node.LineNumber is null)
            return null;

        var lines = node.SourceSnippet.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (node.LineNumber.Value <= 0 || lines.Length == 0)
            return null;

        var requestedLineCount = Math.Clamp(node.LineCount ?? 12, 1, maxLinesPerNode);
        var availableLineCount = Math.Min(requestedLineCount, lines.Length);
        if (availableLineCount <= 0)
            return null;

        var budgetedLineCount = Math.Min(availableLineCount, Math.Max(1, tokenBudget / 18));
        var selected = lines
            .Take(budgetedLineCount)
            .ToArray();
        var truncated = budgetedLineCount < availableLineCount
            || (node.LineCount.HasValue && node.LineCount.Value > budgetedLineCount);
        var numbered = selected
            .Select((line, index) => $"{node.LineNumber.Value + index,4}: {line}")
            .ToArray();
        var text = string.Join(Environment.NewLine, numbered);
        var estimatedTokens = Math.Max(1, text.Length / 4);

        if (estimatedTokens > tokenBudget)
        {
            var maxChars = Math.Max(80, tokenBudget * 4);
            text = text.Length > maxChars ? text[..maxChars] : text;
            truncated = true;
            estimatedTokens = Math.Max(1, text.Length / 4);
        }

        return new SourceSnippet(node, text, estimatedTokens, truncated);
    }

    private static void AppendSourceSnippets(StringBuilder sb, IReadOnlyList<SourceSnippet> snippets, int tokenBudget)
    {
        sb.AppendLine("### Source snippets");

        if (tokenBudget <= 0)
        {
            sb.AppendLine("- Skipped: no remaining token budget after graph context.");
            sb.AppendLine();
            return;
        }

        if (snippets.Count == 0)
        {
            sb.AppendLine("- No indexed source snippets available within budget. Re-index with a version of the indexer that sends bounded `sourceSnippet` data, or increase `maxTokens`.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Budget used: ~{snippets.Sum(s => s.EstimatedTokens):N0}/{tokenBudget:N0} tokens.");
        foreach (var snippet in snippets)
        {
            var node = snippet.Node;
            sb.AppendLine($"#### {node.Type} `{node.Name}` - `{node.FilePath}`:{node.LineNumber}");
            sb.AppendLine("```text");
            sb.AppendLine(snippet.Text);
            if (snippet.Truncated)
                sb.AppendLine("... [truncated to fit source snippet budget]");
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private sealed record SourceSnippet(
        CodeNode Node,
        string Text,
        int EstimatedTokens,
        bool Truncated);
}
