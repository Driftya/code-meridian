using System.Text;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> AnalyzeChangedSubgraphAsync(
        IReadOnlyCollection<string> changedFiles,
        string? projectContext = null,
        int impactDepth = 2,
        int limit = 10,
        bool includeDocs = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedFiles = changedFiles
            .Select(NormalizeChangedPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedFiles.Length == 0)
            return "Provide one or more changed file paths. Git-diff helpers are not implemented in this first slice yet.";

        var boundedDepth = Math.Clamp(impactDepth, 1, 4);
        var boundedLimit = Math.Clamp(limit, 1, 20);
        var changedNodes = await LoadChangedSubgraphNodesAsync(normalizedFiles, projectContext, cancellationToken);

        if (changedNodes.Count == 0)
        {
            return $"No indexed graph nodes matched the provided changed files{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Verify the paths or re-index before relying on changed-subgraph analysis.";
        }

        var productionChangedNodes = changedNodes
            .Where(IsProductionRelevantChangedNode)
            .Where(node => AllowsProfile(node, AnalysisProfile.AgentContext))
            .ToArray();
        var impactSeedNodes = productionChangedNodes
            .Where(IsImpactSeedChangedNode)
            .Take(Math.Max(boundedLimit, 8))
            .ToArray();

        var nodeImpacts = new Dictionary<string, IReadOnlyList<(CodeNode Node, int Distance)>>(StringComparer.Ordinal);
        var nodeTestMatches = new Dictionary<string, IReadOnlyList<(CodeNode Node, string MatchType)>>(StringComparer.Ordinal);
        var impactedNodes = new Dictionary<string, (CodeNode Node, int Distance, int MatchCount)>(StringComparer.Ordinal);

        foreach (var node in impactSeedNodes)
        {
            var impact = await codeGraph.FindImpactAsync(node.Id, boundedDepth, cancellationToken);
            nodeImpacts[node.Id] = impact;

            foreach (var (candidate, distance) in impact.Where(item => AllowsProfile(item.Node, AnalysisProfile.AgentContext)))
            {
                if (changedNodes.Any(changed => changed.Id == candidate.Id))
                    continue;

                if (impactedNodes.TryGetValue(candidate.Id, out var existing))
                {
                    impactedNodes[candidate.Id] = (
                        existing.Node,
                        Math.Min(existing.Distance, distance),
                        existing.MatchCount + 1);
                    continue;
                }

                impactedNodes[candidate.Id] = (candidate, distance, 1);
            }

            var relatedTests = await codeGraph.FindRelatedTestsAsync(node.Id, node.ProjectContext ?? projectContext, cancellationToken);
            nodeTestMatches[node.Id] = relatedTests
                .Where(match => AllowsProfile(match.Node, AnalysisProfile.TestShield))
                .DistinctBy(match => match.Node.Id)
                .ToArray();
        }

        var hotspotTask = productionChangedNodes.Length == 0
            ? Task.FromResult<IReadOnlyList<(CodeNode Node, int FanIn)>>([])
            : codeGraph.FindHotspotsAsync(projectContext, limit: 40, cancellationToken);
        var churnTask = productionChangedNodes.Length == 0
            ? Task.FromResult<IReadOnlyList<(CodeNode Node, int ChangeCount)>>([])
            : codeGraph.FindHighChurnAsync(projectContext, threshold: 3, cancellationToken);
        var architectureTask = productionChangedNodes.Length == 0
            ? Task.FromResult<IReadOnlyList<(CodeNode Source, CodeNode Target, string Violation)>>([])
            : codeGraph.FindArchitectureViolationsAsync(projectContext, cancellationToken);
        var smellTask = productionChangedNodes.Length == 0
            ? Task.FromResult<IReadOnlyList<DependencySmellPath>>([])
            : codeGraph.FindSmellPathsAsync(projectContext, maxDepth: 4, cancellationToken);
        var docsTask = includeDocs
            ? FindChangedSubgraphDocumentsAsync(normalizedFiles, changedNodes, projectContext, boundedLimit, cancellationToken)
            : Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);

        await Task.WhenAll(hotspotTask, churnTask, architectureTask, smellTask, docsTask);

        var hotspotMatches = hotspotTask.Result;
        var churnMatches = churnTask.Result;
        var architectureFindings = architectureTask.Result
            .Where(finding => ChangedFilesContainNode(normalizedFiles, finding.Source) || ChangedFilesContainNode(normalizedFiles, finding.Target))
            .ToArray();
        var smellFindings = smellTask.Result
            .Where(path => ChangedFilesContainPath(normalizedFiles, path))
            .ToArray();
        var relatedDocuments = docsTask.Result;

        var nodeRiskFindings = BuildChangedNodeRiskFindings(
            productionChangedNodes,
            nodeImpacts,
            nodeTestMatches,
            hotspotMatches,
            churnMatches,
            architectureFindings,
            smellFindings)
            .Take(Math.Max(3, boundedLimit))
            .ToArray();

        var directTests = nodeTestMatches.Values
            .SelectMany(matches => matches)
            .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .DistinctBy(node => node.Id)
            .ToArray();
        var testRecommendations = productionChangedNodes
            .SelectMany(node =>
            {
                var matches = nodeTestMatches.TryGetValue(node.Id, out var found)
                    ? found
                    : [];
                var direct = matches
                    .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
                    .Select(match => match.Node);
                var heuristic = matches
                    .Where(match => !match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
                    .Select(match => match.Node);
                return BuildContextTestRecommendations(node, direct, heuristic);
            })
            .GroupBy(item => item.TestNode.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ThenBy(CategoryRank)
            .ThenBy(item => item.TestNode.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(6, boundedLimit))
            .ToArray();
        var missingTests = productionChangedNodes
            .Where(node => !nodeTestMatches.TryGetValue(node.Id, out var matches) || matches.Count == 0)
            .Take(Math.Max(1, boundedLimit))
            .ToArray();
        var overallRisk = DetermineChangedSubgraphRisk(
            productionChangedNodes,
            impactedNodes.Count,
            missingTests.Length,
            architectureFindings.Length,
            smellFindings.Length,
            nodeRiskFindings);

        var sb = new StringBuilder();
        sb.AppendLine($"## Changed Subgraph Analysis{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**Changed files:** {normalizedFiles.Length}");
        sb.AppendLine($"**Changed nodes:** {changedNodes.Count}");
        sb.AppendLine($"**Changed runtimes:** {FormatChangedRuntimeSummary(normalizedFiles)}");
        sb.AppendLine($"**Neighborhood depth:** {boundedDepth}");
        sb.AppendLine($"**Overall risk:** {overallRisk}");
        sb.AppendLine();

        AppendChangedFileSection(sb, normalizedFiles);
        AppendChangedNodeSummarySection(sb, changedNodes, productionChangedNodes);

        if (productionChangedNodes.Length == 0)
        {
            sb.AppendLine("### Risk notes");
            sb.AppendLine("- Only docs/test/configuration-style nodes were matched, so structural impact, architecture, and hotspot signals were suppressed.");
            if (relatedDocuments.Count > 0)
            {
                sb.AppendLine();
                AppendChangedSubgraphDocsSection(sb, relatedDocuments);
            }

            sb.AppendLine();
            sb.AppendLine("> First slice limitation: this tool starts from explicit file paths only. Git working-tree and diff-hunk ingestion are follow-up work.");
            return sb.ToString();
        }

        AppendChangedNodeRiskSection(sb, nodeRiskFindings);
        AppendImpactedNeighborhoodSection(sb, impactedNodes.Values
            .OrderByDescending(item => item.MatchCount)
            .ThenBy(item => item.Distance)
            .ThenBy(item => NodeDisplayRank(item.Node))
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(4, boundedLimit))
            .ToArray());
        AppendFocusedVerificationPlan(sb, testRecommendations);

        sb.AppendLine("### Protection gaps");
        if (missingTests.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var node in missingTests)
                sb.AppendLine($"- `{node.Name}`{FormatLocation(node)} - no related tests were found for this changed node.");
        }

        var suggestedTestCommand = BuildSuggestedTestCommand(
            directTests.Concat(testRecommendations
                .Where(item => item.Category is "Contract/API forwarding tests" or "Integration-level verification")
                .Select(item => item.TestNode))
            .DistinctBy(node => node.Id));
        sb.AppendLine();
        sb.AppendLine("### Suggested test command");
        sb.AppendLine(suggestedTestCommand is null ? "- none" : $"- `{suggestedTestCommand}`");
        sb.AppendLine();

        AppendChangedSubgraphArchitectureSection(sb, architectureFindings, smellFindings);
        AppendChangedSubgraphDocsSection(sb, relatedDocuments);

        sb.AppendLine("> First slice limitation: this tool starts from explicit file paths only. Git working-tree and diff-hunk ingestion are follow-up work.");

        return sb.ToString();
    }

    private async Task<IReadOnlyList<CodeNode>> LoadChangedSubgraphNodesAsync(
        IReadOnlyCollection<string> changedFiles,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var results = new List<CodeNode>();

        foreach (var file in changedFiles)
        {
            var matches = await codeGraph.QueryNodesAsync(
                new CodeGraphQuery
                {
                    ProjectContext = projectContext,
                    FilePathFilter = file,
                    Limit = 200
                },
                cancellationToken);

            results.AddRange(matches);
        }

        return SelectRepresentativeChangedSubgraphNodes(results
            .Where(node => !ChangedSubgraphPathLooksGenerated(node.FilePath))
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.LineNumber ?? int.MaxValue)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    private async Task<IReadOnlyList<KnowledgeDocument>> FindChangedSubgraphDocumentsAsync(
        IReadOnlyCollection<string> changedFiles,
        IReadOnlyCollection<CodeNode> changedNodes,
        string? projectContext,
        int limit,
        CancellationToken cancellationToken)
    {
        var queryTerms = changedFiles
            .Select(Path.GetFileNameWithoutExtension)
            .Concat(changedNodes.Select(node => node.Name))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split([' ', '.', '-', '_', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (queryTerms.Length == 0)
            return [];

        var query = string.Join(" ", queryTerms);
        var docs = await vectorStore.SearchByTextAsync(query, projectContext, topK: Math.Clamp(limit, 4, 8), cancellationToken);
        var excludedSources = changedFiles
            .Select(NormalizeChangedPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return docs
            .Where(doc => !excludedSources.Contains(NormalizeChangedPath(doc.Source)))
            .GroupBy(doc => NormalizeChangedPath(doc.Source), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(doc => doc.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ChangedNodeRiskFinding> BuildChangedNodeRiskFindings(
        IReadOnlyCollection<CodeNode> changedNodes,
        IReadOnlyDictionary<string, IReadOnlyList<(CodeNode Node, int Distance)>> nodeImpacts,
        IReadOnlyDictionary<string, IReadOnlyList<(CodeNode Node, string MatchType)>> nodeTests,
        IReadOnlyCollection<(CodeNode Node, int FanIn)> hotspotMatches,
        IReadOnlyCollection<(CodeNode Node, int ChangeCount)> churnMatches,
        IReadOnlyCollection<(CodeNode Source, CodeNode Target, string Violation)> architectureFindings,
        IReadOnlyCollection<DependencySmellPath> smellFindings)
    {
        var hotspotById = hotspotMatches.ToDictionary(item => item.Node.Id, item => item.FanIn, StringComparer.Ordinal);
        var churnById = churnMatches.ToDictionary(item => item.Node.Id, item => item.ChangeCount, StringComparer.Ordinal);

        return changedNodes
            .Select(node =>
            {
                var reasons = new List<string>();
                var score = 0;

                if (nodeImpacts.TryGetValue(node.Id, out var impacts) && impacts.Count > 0)
                {
                    score += impacts.Count >= 8 ? 4 : impacts.Count >= 3 ? 2 : 1;
                    reasons.Add(impacts.Count == 1
                        ? "1 impacted caller/dependent"
                        : $"{impacts.Count} impacted callers/dependents");
                }

                if (!nodeTests.TryGetValue(node.Id, out var tests) || tests.Count == 0)
                {
                    score += 2;
                    reasons.Add("no related tests found");
                }
                else if (tests.Any(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase)))
                {
                    reasons.Add("direct regression coverage exists");
                }
                else
                {
                    score += 1;
                    reasons.Add("only heuristic test coverage");
                }

                if (hotspotById.TryGetValue(node.Id, out var fanIn))
                {
                    score += fanIn >= 8 ? 3 : fanIn >= 3 ? 2 : 1;
                    reasons.Add($"hotspot fan-in {fanIn}");
                }

                if (churnById.TryGetValue(node.Id, out var changeCount))
                {
                    score += changeCount >= 5 ? 2 : 1;
                    reasons.Add($"{changeCount} indexed changes");
                }

                var touchedArchitecture = architectureFindings.Any(finding => SameFile(finding.Source, node) || SameFile(finding.Target, node));
                if (touchedArchitecture)
                {
                    score += 3;
                    reasons.Add("touches architecture violation");
                }

                if (smellFindings.Any(path => ChangedPathTouchesNode(path, node)))
                {
                    score += 2;
                    reasons.Add("touches dependency smell path");
                }

                if (BuildFreshness(node).Confidence == "Low")
                {
                    score += 1;
                    reasons.Add("low freshness confidence");
                }

                return new ChangedNodeRiskFinding(node, score, ClassifyChangedNodeRisk(score), reasons);
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => NodeDisplayRank(item.Node))
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DetermineChangedSubgraphRisk(
        IReadOnlyCollection<CodeNode> productionChangedNodes,
        int impactedNodeCount,
        int missingTests,
        int architectureFindingCount,
        int smellFindingCount,
        IReadOnlyCollection<ChangedNodeRiskFinding> nodeRisks)
    {
        if (productionChangedNodes.Count == 0)
            return "low";

        var riskPoints = 0;
        if (architectureFindingCount > 0) riskPoints += 3;
        if (smellFindingCount > 0) riskPoints += 2;
        if (missingTests >= 3) riskPoints += 2;
        else if (missingTests > 0) riskPoints += 1;
        if (impactedNodeCount >= 10) riskPoints += 2;
        else if (impactedNodeCount >= 4) riskPoints += 1;
        if (nodeRisks.Any(item => item.Risk == "High")) riskPoints += 2;
        else if (nodeRisks.Any(item => item.Risk == "Medium")) riskPoints += 1;

        return riskPoints >= 6 ? "high"
            : riskPoints >= 2 ? "medium"
            : "low";
    }

    private static void AppendChangedFileSection(StringBuilder sb, IReadOnlyList<string> changedFiles)
    {
        sb.AppendLine("### Changed files");
        foreach (var file in changedFiles.Take(12))
            sb.AppendLine($"- `{file}`");

        if (changedFiles.Count > 12)
            sb.AppendLine($"- ...{changedFiles.Count - 12} more");

        sb.AppendLine();
    }

    private static void AppendChangedNodeSummarySection(
        StringBuilder sb,
        IReadOnlyCollection<CodeNode> changedNodes,
        IReadOnlyCollection<CodeNode> productionChangedNodes)
    {
        sb.AppendLine("### Changed node summary");
        sb.AppendLine($"- Indexed node matches: {changedNodes.Count}");
        sb.AppendLine($"- Production-relevant changed nodes: {productionChangedNodes.Count}");
        sb.AppendLine($"- Docs/test-only matches suppressed: {changedNodes.Count - productionChangedNodes.Count}");
        sb.AppendLine();
    }

    private static void AppendChangedNodeRiskSection(StringBuilder sb, IReadOnlyCollection<ChangedNodeRiskFinding> findings)
    {
        sb.AppendLine($"### Highest-risk changed nodes ({findings.Count})");

        if (findings.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Risk | Node | File | Why |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var finding in findings)
        {
            var file = finding.Node.FilePath is null ? "-" : $"`{finding.Node.FilePath}`";
            sb.AppendLine(
                $"| {finding.Risk} | `{finding.Node.Name}` | {file} | {EscapeTableCell(string.Join("; ", finding.Reasons))} |");
        }

        sb.AppendLine();
    }

    private static void AppendImpactedNeighborhoodSection(
        StringBuilder sb,
        IReadOnlyCollection<(CodeNode Node, int Distance, int MatchCount)> impacted)
    {
        sb.AppendLine($"### Impacted neighborhood ({impacted.Count})");

        if (impacted.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Matches | Distance | Type | Node | File |");
        sb.AppendLine("|---:|---:|---|---|---|");

        foreach (var item in impacted)
        {
            var file = item.Node.FilePath is null ? "-" : $"`{item.Node.FilePath}`";
            sb.AppendLine($"| {item.MatchCount} | {item.Distance} | {item.Node.Type} | `{item.Node.Name}` | {file} |");
        }

        sb.AppendLine();
    }

    private static void AppendChangedSubgraphArchitectureSection(
        StringBuilder sb,
        IReadOnlyCollection<(CodeNode Source, CodeNode Target, string Violation)> architectureFindings,
        IReadOnlyCollection<DependencySmellPath> smellFindings)
    {
        sb.AppendLine("### Architecture and smell findings");

        if (architectureFindings.Count == 0 && smellFindings.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        if (architectureFindings.Count > 0)
        {
            sb.AppendLine($"Architecture violations touching the changed slice ({architectureFindings.Count}):");
            foreach (var finding in architectureFindings.Take(5))
            {
                sb.AppendLine(
                    $"- `{finding.Violation}` - `{finding.Source.Name}`{FormatLocation(finding.Source)} -> `{finding.Target.Name}`{FormatLocation(finding.Target)}");
            }
        }

        if (smellFindings.Count > 0)
        {
            sb.AppendLine($"Dependency smell paths touching the changed slice ({smellFindings.Count}):");
            foreach (var path in smellFindings.Take(5))
                sb.AppendLine($"- `{path.Violation}` - {FormatPathSteps(path.Steps)}");
        }

        sb.AppendLine();
    }

    private static void AppendChangedSubgraphDocsSection(StringBuilder sb, IReadOnlyCollection<KnowledgeDocument> documents)
    {
        sb.AppendLine($"### Docs and feature notes to review ({documents.Count})");

        if (documents.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        foreach (var document in documents.Take(6))
            sb.AppendLine($"- `{document.Source ?? document.Id}`");

        sb.AppendLine();
    }

    private IReadOnlyList<CodeNode> SelectRepresentativeChangedSubgraphNodes(IReadOnlyList<CodeNode> nodes)
    {
        var selected = new List<CodeNode>();

        foreach (var group in nodes.GroupBy(node => NormalizeChangedPath(node.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            var fileNodes = group.Where(node => node.Type == CodeNodeType.File);
            var containerNodes = group.Where(IsChangedSubgraphContainerNode)
                .OrderBy(node => ChangedSubgraphContainerPriority(node.Type))
                .ThenBy(node => node.LineNumber ?? int.MaxValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
            var memberNodes = group.Where(IsChangedSubgraphMemberNode)
                .OrderByDescending(node => node.LineNumber ?? int.MinValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
            var otherNodes = group.Where(node => node.Type != CodeNodeType.File && !IsChangedSubgraphContainerNode(node) && !IsChangedSubgraphMemberNode(node))
                .OrderBy(node => node.LineNumber ?? int.MaxValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);

            var isNonProductionFile = group.All(node => !IsProductionRelevantChangedNode(node));
            var perFileBudget = isNonProductionFile ? 5 : 7;
            var memberBudget = isNonProductionFile ? 2 : 4;

            var perFileSelection = fileNodes.Take(1)
                .Concat(containerNodes.Take(2))
                .Concat(memberNodes.Take(memberBudget))
                .Concat(otherNodes.Take(1))
                .DistinctBy(node => node.Id)
                .Take(perFileBudget);

            selected.AddRange(perFileSelection);
        }

        return selected
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.LineNumber ?? int.MaxValue)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static bool ChangedFilesContainNode(IReadOnlyCollection<string> changedFiles, CodeNode node) =>
        !string.IsNullOrWhiteSpace(node.FilePath)
        && changedFiles.Contains(NormalizeChangedPath(node.FilePath), StringComparer.OrdinalIgnoreCase);

    private static bool ChangedFilesContainPath(IReadOnlyCollection<string> changedFiles, DependencySmellPath path) =>
        ChangedFilesContainNode(changedFiles, path.Source)
        || ChangedFilesContainNode(changedFiles, path.Target)
        || path.Steps.Any(step => ChangedFilesContainNode(changedFiles, step.Node));

    private static bool ChangedPathTouchesNode(DependencySmellPath path, CodeNode node) =>
        SameFile(path.Source, node)
        || SameFile(path.Target, node)
        || path.Steps.Any(step => SameFile(step.Node, node));

    private static string NormalizeChangedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return NormalizePath(path).Trim().TrimStart('.', '/');
    }

    private static string FormatChangedRuntimeSummary(IReadOnlyCollection<string> changedFiles)
    {
        var runtimes = new List<string>();

        if (changedFiles.Any(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            runtimes.Add("C#");
        if (changedFiles.Any(path =>
                path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)))
            runtimes.Add("TypeScript/JS");
        if (changedFiles.Any(path =>
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
            runtimes.Add("Frontend markup/styles");

        return runtimes.Count == 0 ? "unknown" : string.Join(", ", runtimes);
    }

    private static bool ChangedSubgraphPathLooksGenerated(string? filePath)
    {
        var normalized = NormalizeChangedPath(filePath);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsProductionRelevantChangedNode(CodeNode node) =>
        node.FileRole is IndexedFileRole.Source or IndexedFileRole.Configuration or IndexedFileRole.Unknown
        && !IsConfiguredTestNode(node)
        && !LooksLikeDocumentationPath(node.FilePath ?? string.Empty)
        && !ChangedSubgraphPathLooksGenerated(node.FilePath);

    private bool IsImpactSeedChangedNode(CodeNode node) =>
        IsProductionRelevantChangedNode(node)
        && (node.Type is CodeNodeType.Class
            or CodeNodeType.Struct
            or CodeNodeType.Interface
            or CodeNodeType.Method
            or CodeNodeType.Property
            or CodeNodeType.ApiEndpoint
            or CodeNodeType.ConfigurationKey);

    private static bool IsChangedSubgraphContainerNode(CodeNode node) =>
        node.Type is CodeNodeType.Namespace
            or CodeNodeType.Class
            or CodeNodeType.Struct
            or CodeNodeType.Interface
            or CodeNodeType.Enum
            or CodeNodeType.Module
            or CodeNodeType.ConfigurationFile;

    private static bool IsChangedSubgraphMemberNode(CodeNode node) =>
        node.Type is CodeNodeType.Method
            or CodeNodeType.Property
            or CodeNodeType.Field
            or CodeNodeType.Event
            or CodeNodeType.Indexer
            or CodeNodeType.Operator
            or CodeNodeType.Delegate
            or CodeNodeType.ApiEndpoint
            or CodeNodeType.ConfigurationKey;

    private static int ChangedSubgraphContainerPriority(CodeNodeType type) =>
        type switch
        {
            CodeNodeType.Class => 0,
            CodeNodeType.Struct => 1,
            CodeNodeType.Interface => 2,
            CodeNodeType.Module => 3,
            CodeNodeType.Enum => 4,
            CodeNodeType.ConfigurationFile => 5,
            CodeNodeType.Namespace => 6,
            _ => 7
        };

    private static string ClassifyChangedNodeRisk(int score) =>
        score >= 8 ? "High"
        : score >= 4 ? "Medium"
        : "Low";

    private sealed record ChangedNodeRiskFinding(
        CodeNode Node,
        int Score,
        string Risk,
        IReadOnlyList<string> Reasons);
}
