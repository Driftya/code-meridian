using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public async Task<string> SuggestResponsibilitySlicesAsync(
        string target,
        string? projectContext = null,
        int maxSlices = 6,
        bool includeNamespacePlan = true,
        bool includeTestPlan = true,
        bool includeMigrationSteps = true,
        bool includeSourceSnippets = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Target is required.", nameof(target));

        maxSlices = Math.Clamp(maxSlices, 1, 12);
        var targetNode = await ResolveResponsibilityTargetAsync(target, projectContext, cancellationToken);
        if (targetNode is null)
            return $"No class target found for `{target}`{(projectContext is not null ? $" in '{projectContext}'" : "")}. Use `resolve_exact_symbol` first if the target name is ambiguous.";

        var methodNodes = await FindResponsibilityMethodsAsync(targetNode, projectContext, cancellationToken);
        if (methodNodes.Count < 2)
            return BuildResponsibilityDeferResult(
                targetNode,
                "defer_extraction",
                "The target does not have enough indexed method members to form responsibility slices.",
                includeNamespacePlan,
                includeMigrationSteps);

        var methodSignals = new List<ResponsibilityMethodSignals>();
        foreach (var method in methodNodes)
        {
            var context = await codeGraph.GetContextForEditingAsync(method.Id, cancellationToken);
            var tests = await codeGraph.FindRelatedTestsAsync(method.Id, method.ProjectContext ?? projectContext, cancellationToken);
            methodSignals.Add(ResponsibilityMethodSignals.Create(method, context, tests.Select(match => match.Node).ToArray(), IsConfiguredTestNode));
        }

        var docMatches = await vectorStore.SearchByTextAsync(targetNode.Name, targetNode.ProjectContext ?? projectContext, topK: 5, cancellationToken);
        var communityLookup = await TryGetResponsibilityCommunitiesAsync(methodSignals, projectContext, cancellationToken);
        var slices = methodSignals
            .GroupBy(signal => signal.SliceName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var methods = group.ToArray();
                return ResponsibilitySlice.Create(
                    group.Key,
                    methods,
                    docMatches.Select(doc => doc.Source).Where(source => !string.IsNullOrWhiteSpace(source)).Cast<string>().ToArray(),
                    BuildResponsibilityCommunityAdvice(methods, communityLookup));
            })
            .Where(slice => slice.Methods.Count > 1 || slice.Score >= 8)
            .OrderByDescending(slice => slice.Score)
            .ThenBy(slice => slice.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxSlices)
            .ToArray();

        if (slices.Length == 0)
            return BuildResponsibilityDeferResult(
                targetNode,
                "defer_extraction",
                "Indexed methods did not share enough caller, dependency, test, or workflow evidence to recommend a safe extraction.",
                includeNamespacePlan,
                includeMigrationSteps);

        if (slices.All(slice => slice.Confidence == "Low"))
            return BuildResponsibilityDeferResult(
                targetNode,
                "defer_extraction",
                "Indexed methods did not share enough caller, dependency, test, or workflow evidence to recommend a safe extraction.",
                includeNamespacePlan,
                includeMigrationSteps);

        var fanIn = methodSignals.Sum(signal => signal.ProductionCallers.Count);
        var fanOut = methodSignals.Sum(signal => signal.Dependencies.Count);
        var testCount = methodSignals.SelectMany(signal => signal.RelatedTests).DistinctBy(test => test.Id).Count();
        var graphFreshness = DescribeResponsibilityFreshness(targetNode);
        var riskLevel = DescribeResponsibilityRisk(targetNode, fanIn, fanOut, graphFreshness);
        var strategy = SelectResponsibilityStrategy(fanIn, testCount, graphFreshness, slices);
        var namespaceRoot = BuildResponsibilityNamespaceRoot(targetNode);
        var folderRoot = BuildResponsibilityFolderRoot(targetNode);

        var sb = new StringBuilder();
        sb.AppendLine($"## Responsibility Slice Suggestions - `{targetNode.Name}`");
        sb.AppendLine();
        sb.AppendLine($"**Target:** `{targetNode.Id}`");
        if (!string.IsNullOrWhiteSpace(targetNode.FilePath))
            sb.AppendLine($"**File:** `{targetNode.FilePath}`");
        if (!string.IsNullOrWhiteSpace(targetNode.Namespace))
            sb.AppendLine($"**Namespace:** `{targetNode.Namespace}`");
        if (targetNode.LineCount is not null)
            sb.AppendLine($"**Line count:** {targetNode.LineCount.Value.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"**Current risk:** {riskLevel} (fan-in {fanIn}, fan-out {fanOut}, test shield {(testCount > 0 ? "partial" : "missing")}, graph freshness {graphFreshness})");
        sb.AppendLine($"**Recommended strategy:** `{strategy}`");
        sb.AppendLine();

        if (includeNamespacePlan)
        {
            sb.AppendLine($"**Recommended namespace root:** `{namespaceRoot}`");
            sb.AppendLine($"**Recommended folder root:** `{folderRoot}`");
            sb.AppendLine();
        }

        sb.AppendLine("| Slice | Recommended service | Methods | Shared evidence | Confidence |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var slice in slices)
        {
            sb.AppendLine(
                $"| `{slice.Name}` | `{slice.RecommendedTypeName}` | {string.Join("<br>", slice.Methods.Select(method => $"`{method.Method.Name}`"))} | {EscapeTableCell(slice.Reason)} | {slice.Confidence} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Folder Plan");
        sb.AppendLine($"`{folderRoot}/`");
        foreach (var slice in slices)
        {
            sb.AppendLine($"- `{slice.Name}/`");
            sb.AppendLine($"  - `{slice.RecommendedTypeName}.cs`");
            sb.AppendLine($"  - `I{slice.RecommendedTypeName}.cs`");
        }

        if (includeTestPlan)
        {
            sb.AppendLine();
            sb.AppendLine("### Test Impact");
            foreach (var slice in slices)
            {
                var tests = slice.RelatedTests.Count == 0
                    ? "missing direct related tests"
                    : string.Join(", ", slice.RelatedTests.Take(4).Select(test => $"`{test.Name}`"));
                sb.AppendLine($"- `{slice.Name}`: {tests}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### Advisory Community Signals");
        foreach (var slice in slices)
            sb.AppendLine($"- `{slice.Name}`: {slice.CommunitySignal}");

        if (includeMigrationSteps)
        {
            sb.AppendLine();
            sb.AppendLine("### Migration Steps");
            foreach (var step in BuildResponsibilityMigrationSteps(strategy, folderRoot))
                sb.AppendLine($"- {step}");
        }

        sb.AppendLine();
        sb.AppendLine("### Warnings");
        if (graphFreshness != "fresh")
            sb.AppendLine("- Verify source before editing because graph metadata is incomplete or stale.");
        if (!string.IsNullOrWhiteSpace(communityLookup.Warning))
            sb.AppendLine($"- {communityLookup.Warning}");
        if (slices.Any(slice => IsVagueResponsibilityName(slice.RecommendedTypeName)))
            sb.AppendLine("- Rename vague slice services before implementation; prefer use-case names over lifecycle/helper/manager names.");
        sb.AppendLine("- Keep the extracted services in the same architecture layer as the original target.");
        sb.AppendLine("- Do not use partial classes as the default extraction strategy.");
        if (includeSourceSnippets)
            sb.AppendLine("- Source snippets are intentionally not included in this first formatter; use `get_context_for_editing` for bounded snippets.");

        return sb.ToString();
    }

    private async Task<CodeNode?> ResolveResponsibilityTargetAsync(
        string target,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var exactMatches = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                NameFilter = target,
                ProjectContext = projectContext,
                TypeFilter = CodeNodeType.Class,
                Limit = 10
            },
            cancellationToken);

        return exactMatches
            .OrderByDescending(node => string.Equals(node.Name, target, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(node => node.Id.Contains(target, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            ?? (await codeGraph.QueryNodesAsync(
                new CodeGraphQuery
                {
                    SemanticQuery = target,
                    ProjectContext = projectContext,
                    TypeFilter = CodeNodeType.Class,
                    Limit = 10
                },
                cancellationToken)).FirstOrDefault(node => node.Type == CodeNodeType.Class);
    }

    private async Task<IReadOnlyList<CodeNode>> FindResponsibilityMethodsAsync(
        CodeNode targetNode,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var edges = await codeGraph.QueryEdgesAsync(targetNode.Id, depth: 1, cancellationToken);
        var containedMethodIds = edges
            .Where(edge => edge.Type == CodeEdgeType.Contains)
            .Select(edge => string.Equals(edge.SourceId, targetNode.Id, StringComparison.Ordinal) ? edge.TargetId : edge.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        var methods = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = targetNode.ProjectContext ?? projectContext,
                TypeFilter = CodeNodeType.Method,
                Limit = 1000
            },
            cancellationToken);

        var targetFile = NormalizePath(targetNode.FilePath);
        return methods
            .Where(method => AllowsProfile(method, AnalysisProfile.DesignSmells))
            .Where(method => !IsConfiguredTestNode(method))
            .Where(method => containedMethodIds.Contains(method.Id)
                             || (!string.IsNullOrWhiteSpace(targetFile) && string.Equals(NormalizePath(method.FilePath), targetFile, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(method => method.Id)
            .OrderBy(method => method.LineNumber ?? int.MaxValue)
            .ThenBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildResponsibilityDeferResult(
        CodeNode targetNode,
        string strategy,
        string reason,
        bool includeNamespacePlan,
        bool includeMigrationSteps)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Responsibility Slice Suggestions - `{targetNode.Name}`");
        sb.AppendLine($"**Recommended strategy:** `{strategy}`");
        sb.AppendLine();
        sb.AppendLine(reason);
        if (includeNamespacePlan)
        {
            sb.AppendLine();
            sb.AppendLine($"**Recommended namespace root:** `{BuildResponsibilityNamespaceRoot(targetNode)}`");
            sb.AppendLine($"**Recommended folder root:** `{BuildResponsibilityFolderRoot(targetNode)}`");
        }

        if (includeMigrationSteps)
        {
            sb.AppendLine();
            sb.AppendLine("### Migration Steps");
            sb.AppendLine("- Re-index the project if the target is known to have method members.");
            sb.AppendLine("- Add characterization tests before attempting extraction.");
            sb.AppendLine("- Re-run this tool after method, caller, and test edges are present.");
        }

        return sb.ToString();
    }

    private static string BuildResponsibilityNamespaceRoot(CodeNode targetNode)
    {
        var suffix = ToPluralFeatureName(RemoveSuffix(targetNode.Name, "Service"));
        if (string.IsNullOrWhiteSpace(targetNode.Namespace))
            return suffix;

        var parts = targetNode.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
            return string.Join('.', parts.Take(2).Append(suffix));

        return $"{targetNode.Namespace}.{suffix}";
    }

    private static string BuildResponsibilityFolderRoot(CodeNode targetNode)
    {
        var suffix = ToPluralFeatureName(RemoveSuffix(targetNode.Name, "Service"));
        var path = NormalizePath(targetNode.FilePath);
        if (string.IsNullOrWhiteSpace(path))
            return suffix;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var srcIndex = Array.FindIndex(parts, part => string.Equals(part, "src", StringComparison.OrdinalIgnoreCase));
        if (srcIndex >= 0 && parts.Length > srcIndex + 1)
            return string.Join('/', parts.Take(srcIndex + 2).Append(suffix));

        var toolsIndex = Array.FindIndex(parts, part => string.Equals(part, "tools", StringComparison.OrdinalIgnoreCase));
        if (toolsIndex >= 0 && parts.Length > toolsIndex + 1)
            return string.Join('/', parts.Take(toolsIndex + 2).Append(suffix));

        return suffix;
    }

    private static IReadOnlyList<string> BuildResponsibilityMigrationSteps(string strategy, string folderRoot)
    {
        var steps = new List<string>
        {
            $"Create the feature namespace root under `{folderRoot}`.",
            "Extract one slice at a time, starting with the highest-confidence slice.",
            "Move or add slice-specific tests before changing MCP tool, endpoint, or CLI composition."
        };

        if (strategy == "facade_first_extraction")
            steps.Insert(1, "Keep the original service as a temporary facade and delegate to extracted services.");
        else if (strategy == "direct_use_case_replacement")
            steps.Insert(1, "Replace thin callers with direct use-case service dependencies once tests pass.");
        else
            steps.Insert(1, "Defer code movement until graph freshness and test coverage improve.");

        return steps;
    }

    private static string SelectResponsibilityStrategy(
        int fanIn,
        int testCount,
        string graphFreshness,
        IReadOnlyList<ResponsibilitySlice> slices)
    {
        if (graphFreshness == "stale" || slices.All(slice => slice.Confidence == "Low"))
            return "defer_extraction";

        if (fanIn >= 6 || testCount == 0)
            return "facade_first_extraction";

        return "direct_use_case_replacement";
    }

    private static string DescribeResponsibilityRisk(CodeNode targetNode, int fanIn, int fanOut, string graphFreshness)
    {
        if (graphFreshness == "stale" || targetNode.LineCount >= 500 || fanIn >= 10 || fanOut >= 20)
            return "high";

        if (targetNode.LineCount >= 300 || fanIn >= 4 || fanOut >= 8)
            return "medium";

        return "low";
    }

    private static string DescribeResponsibilityFreshness(CodeNode node)
    {
        if (node.FilePath is null || node.UpdatedAt is null)
            return "stale";

        if (node.SourceHash is null || node.LineNumber is null)
            return "partial";

        return "fresh";
    }
}
