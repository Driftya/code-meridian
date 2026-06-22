using System.Collections.Frozen;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.KeywordGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public sealed class PrContextReportService(
    ICodeGraphRepository codeGraph,
    IVectorRepository vectorStore,
    IKeywordExtractionService keywordExtractionService) : IPrContextReportService
{
    private const int MaxDocsToScore = 250;
    private static readonly FrozenSet<CodeNodeType> ImpactSeedNodeTypes = new[]
    {
        CodeNodeType.Class,
        CodeNodeType.Struct,
        CodeNodeType.Interface,
        CodeNodeType.Method,
        CodeNodeType.Property,
        CodeNodeType.ApiEndpoint,
        CodeNodeType.ConfigurationKey
    }.ToFrozenSet();
    private static readonly FrozenSet<CodeNodeType> TestRelevantNodeTypes = new[]
    {
        CodeNodeType.Class,
        CodeNodeType.Struct,
        CodeNodeType.Interface,
        CodeNodeType.Method,
        CodeNodeType.Property,
        CodeNodeType.ApiEndpoint,
        CodeNodeType.ConfigurationKey
    }.ToFrozenSet();

    public async Task<PrContextReport> BuildAsync(
        PrContextReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedFiles = request.ChangedFiles
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedFiles.Length == 0)
        {
            return new PrContextReport(
                request.ProjectContext,
                request.BaseRef,
                request.HeadRef,
                [],
                [],
                [],
                [],
                [],
                [],
                BuildReviewFocus([], [], [], [], [], structuralSignalsSuppressed: false));
        }

        var changedNodes = await LoadChangedNodesAsync(normalizedFiles, request.ProjectContext, cancellationToken);
        var productionChangedNodes = changedNodes.Where(IsProductionRelevantChangedNode).ToArray();
        var impactedNodes = await LoadImpactAsync(productionChangedNodes, request, cancellationToken);
        var missingTests = await LoadMissingTestsAsync(productionChangedNodes, request.ProjectContext, request.Limit, cancellationToken);
        var hotspotWarnings = await LoadHotspotWarningsAsync(productionChangedNodes, request.ProjectContext, cancellationToken);
        var relatedDocuments = request.IncludeDocs
            ? await LoadRelatedDocumentsAsync(normalizedFiles, changedNodes, request.ProjectContext, request.Limit, cancellationToken)
            : [];
        var reviewFocus = BuildReviewFocus(
            normalizedFiles,
            changedNodes,
            impactedNodes,
            missingTests,
            hotspotWarnings,
            productionChangedNodes.Length == 0);

        return new PrContextReport(
            request.ProjectContext,
            request.BaseRef,
            request.HeadRef,
            normalizedFiles,
            changedNodes.Select(ToSummary).ToArray(),
            impactedNodes,
            missingTests,
            hotspotWarnings,
            relatedDocuments,
            reviewFocus);
    }

    private async Task<IReadOnlyList<CodeNode>> LoadChangedNodesAsync(
        IReadOnlyList<string> changedFiles,
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

        return SelectRepresentativeChangedNodes(results
            .Where(node => !SessionPathLooksGenerated(node.FilePath))
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.LineNumber ?? int.MaxValue)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    private async Task<IReadOnlyList<PrContextImpactSummary>> LoadImpactAsync(
        IReadOnlyList<CodeNode> changedNodes,
        PrContextReportRequest request,
        CancellationToken cancellationToken)
    {
        var seedNodes = changedNodes
            .Where(IsImpactSeedNode)
            .Take(Math.Max(request.Limit, 12))
            .ToArray();

        if (seedNodes.Length == 0)
            return [];

        var changedNodeIds = changedNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var impacts = new Dictionary<string, (CodeNode Node, int Distance, int Matches)>(StringComparer.Ordinal);

        foreach (var node in seedNodes)
        {
            var related = await codeGraph.FindImpactAsync(node.Id, Math.Max(1, request.ImpactDepth), cancellationToken);
            foreach (var (candidate, distance) in related)
            {
                if (changedNodeIds.Contains(candidate.Id))
                    continue;

                if (impacts.TryGetValue(candidate.Id, out var existing))
                {
                    impacts[candidate.Id] = (
                        existing.Node,
                        Math.Min(existing.Distance, distance),
                        existing.Matches + 1);
                    continue;
                }

                impacts[candidate.Id] = (candidate, distance, 1);
            }
        }

        return impacts.Values
            .OrderByDescending(item => item.Matches)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.Limit))
            .Select(item => new PrContextImpactSummary(ToSummary(item.Node), item.Distance, item.Matches))
            .ToArray();
    }

    private async Task<IReadOnlyList<PrContextNodeSummary>> LoadMissingTestsAsync(
        IReadOnlyList<CodeNode> changedNodes,
        string? projectContext,
        int limit,
        CancellationToken cancellationToken)
    {
        var missing = new List<PrContextNodeSummary>();

        foreach (var node in changedNodes
                     .Where(IsTestRelevantNode)
                     .Take(Math.Max(limit, 12)))
        {
            var tests = await codeGraph.FindRelatedTestsAsync(node.Id, node.ProjectContext ?? projectContext, cancellationToken);
            if (tests.Count == 0)
                missing.Add(ToSummary(node));
        }

        return missing
            .DistinctBy(node => node.Id)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private async Task<IReadOnlyList<PrContextHotspotWarning>> LoadHotspotWarningsAsync(
        IReadOnlyList<CodeNode> changedNodes,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        if (changedNodes.Count == 0)
            return [];

        var changedNodeIds = changedNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var changedFiles = changedNodes
            .Select(node => NormalizePath(node.FilePath))
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hotspotMatches = await codeGraph.FindHotspotsAsync(projectContext, limit: 40, cancellationToken);
        var churnMatches = await codeGraph.FindHighChurnAsync(projectContext, threshold: 3, cancellationToken);

        var warnings = new Dictionary<string, PrContextHotspotWarning>(StringComparer.Ordinal);

        foreach (var (node, fanIn) in hotspotMatches)
        {
            if (!changedNodeIds.Contains(node.Id) && !changedFiles.Contains(NormalizePath(node.FilePath)))
                continue;

            warnings[node.Id] = new PrContextHotspotWarning(
                ToSummary(node),
                $"High fan-in hotspot ({fanIn} incoming dependencies).",
                FanIn: fanIn,
                ChangeCount: node.ChangeCount);
        }

        foreach (var (node, changeCount) in churnMatches)
        {
            if (!changedNodeIds.Contains(node.Id) && !changedFiles.Contains(NormalizePath(node.FilePath)))
                continue;

            if (warnings.TryGetValue(node.Id, out var existing))
            {
                warnings[node.Id] = existing with
                {
                    Reason = $"{existing.Reason} Frequently re-indexed ({changeCount} changes).",
                    ChangeCount = changeCount
                };
                continue;
            }

            warnings[node.Id] = new PrContextHotspotWarning(
                ToSummary(node),
                $"Frequently re-indexed ({changeCount} changes).",
                ChangeCount: changeCount);
        }

        return warnings.Values
            .OrderByDescending(item => item.FanIn ?? 0)
            .ThenByDescending(item => item.ChangeCount ?? 0)
            .ThenBy(item => item.Node.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private async Task<IReadOnlyList<PrContextRelatedDocument>> LoadRelatedDocumentsAsync(
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<CodeNode> changedNodes,
        string? projectContext,
        int limit,
        CancellationToken cancellationToken)
    {
        var changedKeywords = BuildChangedKeywordWeights(changedFiles, changedNodes);
        if (changedKeywords.Count == 0)
            return [];

        var excludedSources = changedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documents = await vectorStore.ListAsync(projectContext, MaxDocsToScore, cancellationToken);
        var scored = new List<PrContextRelatedDocument>();

        foreach (var document in documents)
        {
            var source = NormalizePath(document.Source);
            if (source.Length == 0 || excludedSources.Contains(source))
                continue;

            var extracted = keywordExtractionService.Extract(ToKeywordSource(document));
            var docWeights = extracted.Keywords.ToDictionary(item => item.NormalizedValue, item => item.Weight, StringComparer.Ordinal);
            var matched = changedKeywords.Keys
                .Where(docWeights.ContainsKey)
                .Select(keyword => new
                {
                    Keyword = keyword,
                    Score = changedKeywords[keyword] * docWeights[keyword] * GetDocumentSourceMultiplier(document, keyword)
                })
                .Where(item => item.Score > 0d)
                .OrderByDescending(item => item.Score)
                .ToArray();

            if (matched.Length == 0)
                continue;

            var score = Math.Round(matched.Sum(item => item.Score), 3, MidpointRounding.AwayFromZero);
            var confidence = ClassifyDocumentConfidence(score);
            if (confidence is null)
                continue;

            scored.Add(new PrContextRelatedDocument(
                document.Id,
                document.Source ?? document.Id,
                confidence,
                score,
                matched.Select(item => item.Keyword).Take(6).ToArray()));
        }

        return scored
            .GroupBy(item => NormalizePath(item.Source), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => ConfidenceRank(item.Confidence))
                .ThenByDescending(item => item.Score)
                .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(item => ConfidenceRank(item.Confidence))
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private Dictionary<string, double> BuildChangedKeywordWeights(
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<CodeNode> changedNodes)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var file in changedFiles)
            MergeKeywords(weights, BuildFileKeywords(file), multiplier: 1d);

        foreach (var node in changedNodes)
            MergeKeywords(weights, keywordExtractionService.Extract(ToKeywordSource(node)).Keywords, multiplier: 1.5d);

        return weights;
    }

    private static IReadOnlyList<ExtractedKeyword> BuildFileKeywords(string file)
    {
        var parts = file
            .Split(['/', '\\', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length >= 3)
            .Select(part => part.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Select(part => new ExtractedKeyword
            {
                Value = part,
                NormalizedValue = part,
                Count = 1,
                Weight = 0.6d,
                Sources = ["filePath"]
            })
            .ToArray();

        return parts;
    }

    private static void MergeKeywords(
        Dictionary<string, double> weights,
        IReadOnlyList<ExtractedKeyword> keywords,
        double multiplier)
    {
        foreach (var keyword in keywords)
        {
            var score = keyword.Weight * multiplier;
            if (weights.TryGetValue(keyword.NormalizedValue, out var existing))
            {
                weights[keyword.NormalizedValue] = Math.Max(existing, score);
                continue;
            }

            weights[keyword.NormalizedValue] = score;
        }
    }

    private static double GetDocumentSourceMultiplier(KnowledgeDocument document, string keyword)
    {
        var source = document.Source ?? string.Empty;
        if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return 1.8d;

        if (source.Contains("docs/features/", StringComparison.OrdinalIgnoreCase))
            return 1.25d;

        if (source.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith("TODO.md", StringComparison.OrdinalIgnoreCase))
            return 1.15d;

        return 1d;
    }

    private static IReadOnlyList<string> BuildReviewFocus(
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<CodeNode> changedNodes,
        IReadOnlyList<PrContextImpactSummary> impactedNodes,
        IReadOnlyList<PrContextNodeSummary> missingTests,
        IReadOnlyList<PrContextHotspotWarning> hotspotWarnings,
        bool structuralSignalsSuppressed)
    {
        var focus = new List<string>();

        if (changedFiles.Count > 0)
            focus.Add($"Review `{changedFiles.Count}` changed file(s) and `{changedNodes.Count}` indexed graph node(s) together.");
        if (impactedNodes.Count >= 5)
            focus.Add($"The graph shows a broad impact surface: `{impactedNodes.Count}` downstream callers or dependents were matched.");
        if (missingTests.Count > 0)
            focus.Add($"Add or update focused regression coverage for `{missingTests.Count}` changed node(s) with no related tests.");
        if (hotspotWarnings.Count > 0)
            focus.Add($"Inspect dependency-heavy or high-churn nodes first; `{hotspotWarnings.Count}` warning(s) were triggered.");
        if (structuralSignalsSuppressed)
            focus.Add("This looks like a docs-only or test-only change; structural impact and hotspot warnings were suppressed to reduce noise.");
        if (focus.Count == 0)
            focus.Add("No strong review risks were detected from the indexed graph; verify the diff and refresh the graph if results look incomplete.");

        return focus;
    }

    private static IReadOnlyList<CodeNode> SelectRepresentativeChangedNodes(IReadOnlyList<CodeNode> nodes)
    {
        var selected = new List<CodeNode>();

        foreach (var group in nodes.GroupBy(node => NormalizePath(node.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            var fileNodes = group.Where(node => node.Type == CodeNodeType.File);
            var containerNodes = group.Where(IsContainerNode)
                .OrderBy(node => ContainerPriority(node.Type))
                .ThenBy(node => node.LineNumber ?? int.MaxValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
            var memberNodes = group.Where(IsMemberNode)
                .OrderByDescending(node => node.LineNumber ?? int.MinValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
            var otherNodes = group.Where(node => node.Type != CodeNodeType.File && !IsContainerNode(node) && !IsMemberNode(node))
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

    private static PrContextNodeSummary ToSummary(CodeNode node) =>
        new(
            node.Id,
            node.Name,
            node.Type.ToString(),
            node.FilePath,
            node.ProjectContext,
            node.LineNumber,
            node.LineCount);

    private static KeywordSourceNode ToKeywordSource(CodeNode node) =>
        new()
        {
            Id = node.Id,
            ProjectContext = node.ProjectContext,
            Kind = node.Type == CodeNodeType.ApiEndpoint ? "ApiEndpoint" : "CodeNode",
            TextBySource = new Dictionary<string, string?>
            {
                ["name"] = node.Name,
                ["summary"] = node.Summary,
                ["namespace"] = node.Namespace,
                ["filePath"] = node.FilePath,
                ["type"] = node.Type.ToString()
            }
        };

    private static KeywordSourceNode ToKeywordSource(KnowledgeDocument document) =>
        new()
        {
            Id = document.Id,
            ProjectContext = document.ProjectContext,
            Kind = "KnowledgeDocument",
            TextBySource = new Dictionary<string, string?>
            {
                ["title"] = Path.GetFileNameWithoutExtension(document.Source ?? document.Id),
                ["content"] = document.Content,
                ["source"] = document.Source,
                ["kind"] = "KnowledgeDocument"
            }
        };

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim()
            .Replace('\\', '/')
            .TrimStart('.', '/');
    }

    private static bool SessionPathLooksGenerated(string? filePath)
    {
        var normalized = NormalizePath(filePath);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestRelevantNode(CodeNode node) =>
        TestRelevantNodeTypes.Contains(node.Type)
        && node.FileRole != IndexedFileRole.Test
        && !string.IsNullOrWhiteSpace(node.FilePath)
        && !node.FilePath.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
        && !node.FilePath.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsProductionRelevantChangedNode(CodeNode node) =>
        node.FileRole is IndexedFileRole.Source or IndexedFileRole.Configuration or IndexedFileRole.Unknown
        && !IsClearlyTestPath(node.FilePath);

    private static bool IsImpactSeedNode(CodeNode node) =>
        IsProductionRelevantChangedNode(node) && ImpactSeedNodeTypes.Contains(node.Type);

    private static bool IsContainerNode(CodeNode node) =>
        node.Type is CodeNodeType.Namespace
            or CodeNodeType.Class
            or CodeNodeType.Struct
            or CodeNodeType.Interface
            or CodeNodeType.Enum
            or CodeNodeType.Module
            or CodeNodeType.ConfigurationFile;

    private static bool IsMemberNode(CodeNode node) =>
        node.Type is CodeNodeType.Method
            or CodeNodeType.Property
            or CodeNodeType.Field
            or CodeNodeType.Event
            or CodeNodeType.Indexer
            or CodeNodeType.Operator
            or CodeNodeType.Delegate
            or CodeNodeType.ApiEndpoint
            or CodeNodeType.ConfigurationKey;

    private static int ContainerPriority(CodeNodeType type) =>
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

    private static bool IsClearlyTestPath(string? filePath)
    {
        var normalized = NormalizePath(filePath);
        return normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("tests.cs", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".tests.cs", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".spec.tsx", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ClassifyDocumentConfidence(double score) =>
        score switch
        {
            >= 8d => "High",
            >= 4d => "Medium",
            _ => null
        };

    private static int ConfidenceRank(string confidence) =>
        confidence switch
        {
            "High" => 2,
            "Medium" => 1,
            _ => 0
        };
}
