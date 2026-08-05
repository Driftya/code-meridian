using System.Text;

namespace CodeMeridian.Application.Services;

public sealed record TestShieldResult(
    string ContractVersion,
    string RequestedNodeId,
    string? ProjectContext,
    int PathDepth,
    bool TargetFound,
    GraphNodeResult? Target,
    RelationshipCompletenessResult? RelationshipCompleteness,
    IReadOnlyList<TestShieldFindingResult> DirectTests,
    IReadOnlyList<TestShieldFindingResult> PrimaryTests,
    IReadOnlyList<TestShieldFindingResult> SecondaryTests,
    IReadOnlyList<TestRecommendationResult> FocusedRecommendations,
    IReadOnlyList<GraphNodeResult> UnshieldedNodes,
    string? SuggestedTestCommand,
    bool Truncated)
{
    public string ToMarkdown()
    {
        if (!TargetFound)
        {
            return $"Node `{RequestedNodeId}` not found in the graph. "
                   + "Run `query_codebase` or `resolve_exact_symbol` to find the correct target before checking its test shield.";
        }

        var target = Target!;
        var builder = new StringBuilder();
        RelationshipCompleteness!.AppendWarning(builder);
        builder.AppendLine($"## Test Shield Map - `{target.Name}`");
        builder.AppendLine($"**Target:** {target.Type} `{target.Name}`");
        builder.AppendLine($"**File:** `{target.FilePath ?? "—"}`{(target.LineNumber.HasValue ? $":{target.LineNumber}" : string.Empty)}");
        builder.AppendLine($"**Path depth:** {PathDepth}");
        builder.AppendLine($"**Shield summary:** {DirectTests.Count} direct, {PrimaryTests.Count} primary, {SecondaryTests.Count} secondary, {UnshieldedNodes.Count} unshielded path nodes");
        builder.AppendLine();

        AppendDirectTests(builder, target);
        AppendFindings(builder, "Primary verification tests", PrimaryTests);
        AppendFocusedPlan(builder);
        AppendFindings(builder, "Secondary shield awareness", SecondaryTests);
        AppendUnshielded(builder);
        builder.AppendLine("### Suggested test command");
        builder.AppendLine(SuggestedTestCommand is null ? "- none" : $"- `{SuggestedTestCommand}`");
        builder.AppendLine();
        builder.AppendLine("> Direct shield means a test directly calls the target. The focused verification plan separates direct regression tests, contract/API forwarding checks, integration-level verification, and heuristic shield tests so the first run stays small. Secondary shield awareness keeps broader matches visible without mixing them into the first-run verification set. Unshielded path nodes are the best seams for new characterization tests before changing behavior.");
        return builder.ToString();
    }

    private void AppendDirectTests(StringBuilder builder, GraphNodeResult target)
    {
        builder.AppendLine($"### Direct test shield ({DirectTests.Count})");
        if (DirectTests.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var finding in DirectTests)
                builder.AppendLine($"- **{finding.TestNode.Type}** `{finding.TestNode.Name}`{finding.TestNode.FormatLocation()} — direct `Calls` edge to `{target.Name}`");
        }
        builder.AppendLine();
    }

    private static void AppendFindings(
        StringBuilder builder,
        string title,
        IReadOnlyCollection<TestShieldFindingResult> findings)
    {
        builder.AppendLine($"### {title} ({findings.Count})");
        if (findings.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var finding in findings)
                builder.AppendLine($"- **{finding.TestNode.Type}** `{finding.TestNode.Name}`{finding.TestNode.FormatLocation()} — {finding.Reason}");
        }
        builder.AppendLine();
    }

    private void AppendFocusedPlan(StringBuilder builder)
    {
        builder.AppendLine($"### Focused verification plan ({FocusedRecommendations.Count})");
        if (FocusedRecommendations.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (var category in new[]
                 {
                     "Direct regression tests",
                     "Contract/API forwarding tests",
                     "Integration-level verification",
                     "Heuristic shield tests"
                 })
        {
            var recommendations = FocusedRecommendations
                .Where(item => item.Category == category)
                .Take(3)
                .ToArray();
            if (recommendations.Length == 0)
                continue;

            builder.AppendLine($"{category}:");
            foreach (var item in recommendations)
                builder.AppendLine($"- **{item.TestNode.Type}** `{item.TestNode.Name}`{item.TestNode.FormatLocation()} — {item.Reason}");
        }
        builder.AppendLine();
    }

    private void AppendUnshielded(StringBuilder builder)
    {
        builder.AppendLine($"### Unshielded path nodes ({UnshieldedNodes.Count})");
        if (UnshieldedNodes.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var node in UnshieldedNodes)
                builder.AppendLine($"- **{node.Type}** `{node.Name}`{node.FormatLocation()} — no direct or heuristic related tests found");
        }
        builder.AppendLine();
    }
}
