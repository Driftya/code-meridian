using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> ReplaceSurfaceAsync(
        string fromDependency,
        string toDependency,
        string? projectContext = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromDependency) || string.IsNullOrWhiteSpace(toDependency))
            return "Provide both the current dependency and the replacement dependency so CodeMeridian can group the replacement surface.";

        var rawMatches = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = fromDependency,
                ProjectContext = projectContext,
                Limit = Math.Clamp(limit * 5, 25, 200)
            },
            cancellationToken);

        var matches = rawMatches
            .Where(IsReplacementUsageNode)
            .Where(node => NodeMatchesDependency(node, fromDependency))
            .Where(node => !IsConfiguredTestNode(node))
            .DistinctBy(node => node.Id)
            .Take(Math.Clamp(limit * 3, 10, 100))
            .ToArray();

        if (matches.Length == 0)
            return $"No indexed usage nodes found for `{fromDependency}`{(projectContext is not null ? $" in `{projectContext}`" : "")}. Re-index the project, or use a dependency/name string that appears in indexed node names, summaries, namespaces, or file paths.";

        var candidates = new List<ReplaceSurfaceCandidate>(matches.Length);
        foreach (var node in matches)
        {
            var relatedTests = await codeGraph.FindRelatedTestsAsync(node.Id, node.ProjectContext ?? projectContext, cancellationToken);
            var diagnostics = await codeGraph.FindDiagnosticsForNodeAsync(node.Id, cancellationToken);
            var ctx = await codeGraph.GetContextForEditingAsync(node.Id, cancellationToken);

            candidates.Add(BuildReplaceSurfaceCandidate(
                node,
                relatedTests.Select(item => item.Node).DistinctBy(test => test.Id).ToArray(),
                diagnostics.Where(diag => SameFile(diag, node)).DistinctBy(diag => diag.Id).ToArray(),
                ctx));
        }

        var clusters = candidates
            .GroupBy(candidate => candidate.Module, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildReplaceSurfaceCluster(group.Key, group.ToArray()))
            .OrderBy(cluster => cluster.RiskRank)
            .ThenByDescending(cluster => cluster.Findings.Count)
            .ThenBy(cluster => cluster.Module, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();

        var safeClusters = clusters.Where(cluster => cluster.IsSafe).ToArray();
        var riskyClusters = clusters.Where(cluster => !cluster.IsSafe).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"## Replacement Surface - `{fromDependency}` -> `{toDependency}`");
        if (!string.IsNullOrWhiteSpace(projectContext))
            sb.AppendLine($"**Project:** `{projectContext}`");
        sb.AppendLine($"**Usage nodes inspected:** {matches.Length}");
        sb.AppendLine($"**Groups:** {safeClusters.Length} safe, {riskyClusters.Length} risky");
        sb.AppendLine();

        AppendReplaceSurfaceSection(sb, "Safe replacement groups", safeClusters, toDependency);
        AppendReplaceSurfaceSection(sb, "Risky replacement groups", riskyClusters, toDependency);

        sb.AppendLine("> Safe-first heuristic: groups are marked risky when usage crosses API/contracts/infrastructure boundaries, lacks nearby tests, or already has file-local diagnostics. Verify exact edit order with `plan_edit_route` or `build_minimal_context` before changing shared groups.");
        return sb.ToString();
    }

    private static bool IsReplacementUsageNode(CodeNode node) =>
        node.Type is CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.Method or CodeNodeType.Property or CodeNodeType.Field or CodeNodeType.File or CodeNodeType.Module;

    private static bool NodeMatchesDependency(CodeNode node, string dependency)
    {
        var haystacks = new[]
        {
            node.Name,
            node.Namespace,
            node.FilePath,
            node.Summary,
            node.Id
        };

        if (haystacks.Any(value => TextMatches(value, dependency)))
            return true;

        var dependencyParts = dependency
            .Split(['.', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return dependencyParts.Any(part => haystacks.Any(value => TextMatches(value, part)));
    }

    private ReplaceSurfaceCandidate BuildReplaceSurfaceCandidate(
        CodeNode node,
        IReadOnlyCollection<CodeNode> relatedTests,
        IReadOnlyCollection<CodeNode> diagnostics,
        EditingContext context)
    {
        var reasons = new List<string>();
        var riskScore = 0;

        if (IsContractNode(node) || context.Interfaces.Count > 0)
        {
            reasons.Add("touches shared contract");
            riskScore += 3;
        }

        if (IsApiNode(node) || context.Callers.Any(IsApiNode))
        {
            reasons.Add("crosses API boundary");
            riskScore += 2;
        }

        if (IsInfrastructureNode(node) || context.Callees.Any(IsInfrastructureNode))
        {
            reasons.Add("touches infrastructure adapter");
            riskScore += 2;
        }

        if (diagnostics.Count > 0)
        {
            reasons.Add($"{diagnostics.Count} nearby diagnostics");
            riskScore += 2;
        }

        if (relatedTests.Count == 0)
        {
            reasons.Add("no related tests");
            riskScore += 2;
        }
        else
        {
            reasons.Add($"{relatedTests.Count} related tests");
        }

        var freshness = BuildFreshness(node);
        if (freshness.Confidence == "Low")
        {
            reasons.Add("stale graph metadata");
            riskScore += 2;
        }

        if (context.Callers.Any(caller => !SameProject(node, caller)))
        {
            reasons.Add("cross-project callers");
            riskScore += 2;
        }

        var module = ResolveReplacementModule(node);
        return new ReplaceSurfaceCandidate(
            module,
            node,
            relatedTests.ToArray(),
            diagnostics.ToArray(),
            riskScore <= 2,
            riskScore,
            string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private ReplaceSurfaceCluster BuildReplaceSurfaceCluster(
        string module,
        IReadOnlyCollection<ReplaceSurfaceCandidate> findings)
    {
        var ordered = findings
            .OrderByDescending(finding => finding.RiskScore)
            .ThenBy(finding => finding.Node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var riskScore = ordered.Sum(finding => finding.RiskScore);
        var riskReasons = ordered
            .Where(finding => !finding.IsSafe)
            .Select(finding => finding.Reason)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var safeReason = ordered
            .Select(finding => finding.Reason)
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
            ?? "isolated usage with nearby tests";
        var riskLabel = riskScore >= 8 ? "High"
            : riskScore >= 4 ? "Medium"
            : "Low";

        return new ReplaceSurfaceCluster(
            module,
            ordered.All(finding => finding.IsSafe),
            ordered,
            riskScore,
            ordered.All(finding => finding.IsSafe) ? "Low" : riskLabel,
            ordered.All(finding => finding.IsSafe) ? safeReason : string.Join("; ", riskReasons));
    }

    private static string ResolveReplacementModule(CodeNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Namespace))
        {
            var nsParts = node.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (nsParts.Length >= 2)
                return $"{nsParts[0]}.{nsParts[1]}";
            if (nsParts.Length == 1)
                return nsParts[0];
        }

        if (!string.IsNullOrWhiteSpace(node.FilePath))
        {
            var segments = node.FilePath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 3)
                return $"{segments[0]}/{segments[1]}/{segments[2]}";
            if (segments.Length >= 2)
                return $"{segments[0]}/{segments[1]}";
            if (segments.Length == 1)
                return segments[0];
        }

        return node.ProjectContext ?? "Unknown";
    }

    private static void AppendReplaceSurfaceSection(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<ReplaceSurfaceCluster> clusters,
        string toDependency)
    {
        sb.AppendLine($"### {title} ({clusters.Count})");

        if (clusters.Count == 0)
        {
            sb.AppendLine("- None");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Module | Risk | Files | Usage nodes | Tests | Why | Next move |");
        sb.AppendLine("|---|---|---:|---|---|---|---|");

        foreach (var cluster in clusters)
        {
            var files = cluster.Findings
                .Select(finding => finding.Node.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var usages = string.Join("<br>", cluster.Findings.Take(3).Select(finding => $"`{finding.Node.Name}`"));
            var tests = cluster.Findings
                .SelectMany(finding => finding.RelatedTests)
                .Select(test => $"`{test.Name}`")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .DefaultIfEmpty("—");
            var nextMove = cluster.IsSafe
                ? $"swap to `{toDependency}` inside one module, then run targeted tests"
                : $"use `{toDependency}` only after route + boundary review";

            sb.AppendLine(
                $"| `{cluster.Module}` | {cluster.RiskLabel} | {files} | {usages} | {string.Join("<br>", tests)} | {EscapeTableCell(cluster.Reason)} | {EscapeTableCell(nextMove)} |");
        }

        sb.AppendLine();
    }

    private sealed record ReplaceSurfaceCandidate(
        string Module,
        CodeNode Node,
        IReadOnlyCollection<CodeNode> RelatedTests,
        IReadOnlyCollection<CodeNode> Diagnostics,
        bool IsSafe,
        int RiskScore,
        string Reason);

    private sealed record ReplaceSurfaceCluster(
        string Module,
        bool IsSafe,
        IReadOnlyList<ReplaceSurfaceCandidate> Findings,
        int RiskRank,
        string RiskLabel,
        string Reason);
}
