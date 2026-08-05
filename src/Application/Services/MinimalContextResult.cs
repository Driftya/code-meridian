using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record MinimalContextResult(
    string ContractVersion,
    string RequestedTarget,
    string? Goal,
    bool TargetFound,
    GraphNodeResult? Target,
    string DetailLevel,
    bool IncludeTests,
    bool IncludeExternalConcepts,
    bool IncludeSourceSnippets,
    bool ExplainPaths,
    RelationshipCompletenessResult? RelationshipCompleteness,
    MinimalContextBudgetResult Budget,
    IReadOnlyList<GraphNodeResult> Callers,
    IReadOnlyList<GraphNodeResult> Callees,
    IReadOnlyList<GraphNodeResult> Interfaces,
    IReadOnlyList<MinimalContextDistanceResult> Impact,
    IReadOnlyList<MinimalContextDistanceResult> Downstream,
    IReadOnlyList<GraphNodeResult> CoverageGaps,
    IReadOnlyList<TestRecommendationResult> Tests,
    string? SuggestedTestCommand,
    IReadOnlyList<string> Files,
    IReadOnlyList<MinimalContextExplainedFileResult> ExplainedFiles,
    IReadOnlyList<MinimalContextSnippetResult> Snippets,
    IReadOnlyList<MinimalContextDegradationResult> Degradations,
    MinimalContextTruncationResult Truncation)
{
    internal static MinimalContextResult NotFound(
        string requestedTarget,
        string? goal,
        int maxTokens,
        bool includeTests,
        bool includeExternalConcepts,
        bool includeSourceSnippets,
        bool explainPaths,
        ContextDetailLevel detailLevel) =>
        new(
            "1.0", requestedTarget, goal, false, null, detailLevel.ToString(),
            includeTests, includeExternalConcepts, includeSourceSnippets, explainPaths, null,
            new MinimalContextBudgetResult(maxTokens, 0, 0, 0, true, 128 * 1024, "Unknown", "Target not found.", "Unknown", "No graph context was available."),
            [], [], [], [], [], [], [], null, [], [], [], [],
            new MinimalContextTruncationResult(false, false, false, false, false, false, false, false, false));

    public string ToMarkdown(ContextDetailLevel detailLevel)
    {
        if (!TargetFound)
            return $"Target `{RequestedTarget}` not found in the graph. Run query_codebase first to find the correct node ID.";

        var target = Target!;
        var builder = new StringBuilder();
        RelationshipCompleteness!.AppendWarning(builder);
        builder.AppendLine($"## Minimal Context Pack — `{target.Name}`");
        if (!string.IsNullOrWhiteSpace(Goal))
            builder.AppendLine($"**Goal:** {Goal}");
        builder.AppendLine($"**Budget:** {Budget.RequestedTokens} tokens | **Estimated:** {Budget.EstimatedTokens:N0} tokens | **Detail:** {detailLevel}");
        builder.AppendLine($"**Complexity:** {Budget.Complexity} | **Model guidance:** {Budget.ModelGuidance}");
        builder.AppendLine($"**Expansion risk:** {Budget.ExpansionRisk} — {Budget.Reason}");
        builder.AppendLine($"**Target:** {target.Type} `{target.Name}`");
        builder.AppendLine($"**File:** `{target.FilePath ?? "—"}`{(target.LineNumber.HasValue ? $":{target.LineNumber}" : string.Empty)}{(target.LineCount.HasValue ? $" ({target.LineCount} lines)" : string.Empty)}");
        if (!string.IsNullOrWhiteSpace(target.Summary))
            builder.AppendLine($"**Summary:** {target.Summary}");
        builder.AppendLine();

        AppendNodeList(builder, "Direct callers", Callers, detailLevel, "who will be affected by signature or behavior changes");
        AppendNodeList(builder, "Direct callees", Callees, detailLevel, "dependencies this target relies on");
        AppendNodeList(builder, "Interfaces", Interfaces, detailLevel, "contracts related to this target");
        AppendDistanceList(builder, "Near impact", Impact, detailLevel, "transitive callers within 2 hops");
        AppendDistanceList(builder, "Near downstream", Downstream, detailLevel, "dependencies within 2 hops");

        if (IncludeTests)
        {
            AppendNodeList(builder, "Relevant coverage gaps", CoverageGaps, detailLevel, "heuristic matches by same file/namespace/target");
            AppendTests(builder, detailLevel);
        }

        if (Files.Count > 0)
        {
            if (ExplainPaths && ExplainedFiles.Count > 0)
                AppendExplainedFiles(builder);
            else
                AppendFiles(builder);
        }

        if (IncludeSourceSnippets)
            AppendSnippets(builder);

        if (IncludeExternalConcepts)
            builder.AppendLine("> External concepts are included when present in callers/callees/impact/downstream graph results.");

        AppendDegradations(builder);
        builder.AppendLine($"> Token estimate is approximate. {(Budget.FitsRequestedBudget ? "Current pack fits the requested budget." : "Consider Summary detail, fewer optional sections, or a larger context budget.")}");
        return builder.ToString();
    }

    private static void AppendNodeList(
        StringBuilder builder,
        string title,
        IReadOnlyCollection<GraphNodeResult> nodes,
        ContextDetailLevel detailLevel,
        string note)
    {
        builder.AppendLine($"### {title} ({nodes.Count})");
        if (nodes.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }
        if (detailLevel == ContextDetailLevel.Summary)
        {
            builder.AppendLine($"- {nodes.Count} nodes ({note})");
            builder.AppendLine();
            return;
        }
        foreach (var node in nodes.Take(detailLevel == ContextDetailLevel.Full ? 50 : 10))
            builder.AppendLine($"- **{node.Type}** `{node.Name}`{node.FormatLocation()}");
        if (detailLevel != ContextDetailLevel.Full && nodes.Count > 10)
            builder.AppendLine($"- ...{nodes.Count - 10} more");
        builder.AppendLine();
    }

    private static void AppendDistanceList(
        StringBuilder builder,
        string title,
        IReadOnlyCollection<MinimalContextDistanceResult> nodes,
        ContextDetailLevel detailLevel,
        string note)
    {
        builder.AppendLine($"### {title} ({nodes.Count})");
        if (nodes.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }
        if (detailLevel == ContextDetailLevel.Summary)
        {
            builder.AppendLine($"- {nodes.Count} nodes ({note})");
            builder.AppendLine();
            return;
        }
        foreach (var item in nodes.Take(detailLevel == ContextDetailLevel.Full ? 50 : 10))
            builder.AppendLine($"- d{item.Distance}: **{item.Node.Type}** `{item.Node.Name}`{item.Node.FormatLocation()}");
        if (detailLevel != ContextDetailLevel.Full && nodes.Count > 10)
            builder.AppendLine($"- ...{nodes.Count - 10} more");
        builder.AppendLine();
    }

    private void AppendTests(StringBuilder builder, ContextDetailLevel detailLevel)
    {
        builder.AppendLine($"### Relevant tests ({Tests.Count})");
        if (Tests.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }
        if (detailLevel == ContextDetailLevel.Summary)
        {
            var summary = string.Join(", ", Tests.GroupBy(item => item.Category, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Count()} {group.Key.ToLowerInvariant()}"));
            builder.AppendLine($"- {summary}");
            if (SuggestedTestCommand is not null)
                builder.AppendLine($"- Suggested command: `{SuggestedTestCommand}`");
            builder.AppendLine();
            return;
        }
        foreach (var category in new[] { "Direct regression tests", "Contract/API forwarding tests", "Integration-level verification", "Heuristic shield tests" })
        {
            var bucket = Tests.Where(item => item.Category == category).Take(3).ToArray();
            if (bucket.Length == 0)
                continue;
            builder.AppendLine($"{category}:");
            foreach (var item in bucket)
                builder.AppendLine($"- **{item.TestNode.Type}** `{item.TestNode.Name}`{item.TestNode.FormatLocation()} — {item.Reason}");
        }
        builder.AppendLine($"Suggested command: {(SuggestedTestCommand is null ? "none" : $"`{SuggestedTestCommand}`")}");
        builder.AppendLine();
    }

    private void AppendFiles(StringBuilder builder)
    {
        builder.AppendLine("### Files likely needed");
        foreach (var file in Files)
            builder.AppendLine($"- `{file}`");
        builder.AppendLine();
    }

    private void AppendExplainedFiles(StringBuilder builder)
    {
        builder.AppendLine($"### File inclusion paths ({ExplainedFiles.Count})");
        foreach (var file in ExplainedFiles)
        {
            var details = new List<string> { file.Reason, $"path: {file.EvidencePath}" };
            if (file.Diagnostics.Count > 0)
                details.Add($"nearby diagnostics: {string.Join(", ", file.Diagnostics.Select(name => $"`{name}`"))}");
            if (file.NearbyTests.Count > 0)
                details.Add($"nearby tests: {string.Join(", ", file.NearbyTests.Select(name => $"`{name}`"))}");
            builder.AppendLine($"- `{file.FilePath}` — {string.Join("; ", details)}");
        }
        builder.AppendLine();
    }

    private void AppendSnippets(StringBuilder builder)
    {
        builder.AppendLine("### Source snippets");
        if (Budget.SourceSnippetBudgetTokens <= 0)
        {
            builder.AppendLine("- Skipped: no remaining token budget after graph context.");
            builder.AppendLine();
            return;
        }
        if (Snippets.Count == 0)
        {
            builder.AppendLine("- No indexed source snippets available within budget. Re-index with a version of the indexer that sends bounded `sourceSnippet` data, or increase `maxTokens`.");
            builder.AppendLine();
            return;
        }
        builder.AppendLine($"Budget used: ~{Budget.SourceSnippetEstimatedTokens:N0}/{Budget.SourceSnippetBudgetTokens:N0} tokens.");
        foreach (var snippet in Snippets)
        {
            builder.AppendLine($"#### {snippet.Node.Type} `{snippet.Node.Name}` - `{snippet.Node.FilePath}`:{snippet.Node.LineNumber}");
            builder.AppendLine("```text");
            builder.AppendLine(snippet.MarkdownText);
            if (snippet.Truncated)
                builder.AppendLine("... [truncated to fit source snippet budget]");
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private void AppendDegradations(StringBuilder builder)
    {
        if (Degradations.Count == 0)
            return;
        builder.AppendLine("### Degraded mode");
        builder.AppendLine("`context_pack_status=degraded`");
        foreach (var degradation in Degradations)
        {
            builder.AppendLine($"- failed_step: `{degradation.Step}`");
            builder.AppendLine($"- exception: `{degradation.ExceptionType}`");
        }
        builder.AppendLine("- fallback: use `resolve_exact_symbol`, `find_impact`, and `find_test_shield` for exact target, blast radius, and test coverage when one or more context-pack sub-steps fail.");
        builder.AppendLine();
    }
}
