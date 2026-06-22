using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public async Task<string> FindImplementationPatternsAsync(
        string query,
        string? projectContext = null,
        bool excludeTests = true,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Provide a feature or flow query so CodeMeridian can look for structural implementation patterns.";

        var boundedLimit = Math.Clamp(limit, 1, 10);
        var seeds = await CollectImplementationPatternSeedsAsync(query, projectContext, excludeTests, cancellationToken);
        if (seeds.Count == 0)
        {
            return $"No implementation patterns found for `{query}`. " +
                   "Try a more specific flow description, enable embeddings, or re-index before relying on structural pattern search.";
        }

        var candidates = new List<ImplementationPatternCandidate>();
        foreach (var seed in seeds.Take(Math.Clamp(boundedLimit * 3, 6, 18)))
        {
            var candidate = await BuildImplementationPatternCandidateAsync(seed, projectContext, cancellationToken);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        var ranked = candidates
            .Where(candidate => candidate.StructuralBucketCount >= 2)
            .GroupBy(candidate => candidate.ScopeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.TotalScore)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToArray();

        if (ranked.Length == 0)
        {
            return $"CodeMeridian found related nodes for `{query}`, but none had enough shared structure to promote as reusable implementation patterns. " +
                   "Try a more specific query or re-index richer relationship data.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Structural Implementation Patterns - `{query}`");
        sb.AppendLine($"**Seed retrieval:** {DescribePatternSeedRetrieval(seeds)}");
        sb.AppendLine($"**{ranked.Length}** ranked patterns matched by structural shape.");
        sb.AppendLine();
        sb.AppendLine("| Rank | Confidence | Shape | Pattern | Structural evidence | Tests | Seed | File |");
        sb.AppendLine("|---:|---|---|---|---|---:|---|---|");

        var rank = 1;
        foreach (var candidate in ranked)
        {
            sb.AppendLine(
                $"| {rank++} | {candidate.Confidence} | {EscapeTableCell(candidate.Shape)} | `{candidate.DisplayName}` | {EscapeTableCell(candidate.EvidenceSummary)} | {candidate.TestCount} | {candidate.SeedSummary} | `{candidate.PrimaryFile}` |");
        }

        sb.AppendLine();
        sb.AppendLine("### Pattern details");
        foreach (var candidate in ranked)
        {
            sb.AppendLine($"#### `{candidate.DisplayName}`");
            sb.AppendLine($"- Shape: {candidate.Shape}");
            sb.AppendLine($"- Confidence: {candidate.Confidence}");
            sb.AppendLine($"- Seed: {candidate.SeedSummary}");
            sb.AppendLine($"- Entry points: {FormatPatternNodeList(candidate.EntryNodes)}");
            sb.AppendLine($"- Application/domain: {FormatPatternNodeList(candidate.BehaviorNodes)}");
            if (candidate.ContractNodes.Count > 0)
                sb.AppendLine($"- Contracts: {FormatPatternNodeList(candidate.ContractNodes)}");
            if (candidate.StoreNodes.Count > 0)
                sb.AppendLine($"- Repository/store: {FormatPatternNodeList(candidate.StoreNodes)}");
            if (candidate.ExternalBoundaryNodes.Count > 0)
                sb.AppendLine($"- External boundary: {FormatPatternNodeList(candidate.ExternalBoundaryNodes)}");
            sb.AppendLine($"- Tests: {FormatPatternNodeList(candidate.TestNodes)}");
            sb.AppendLine($"- Why: {candidate.EvidenceSummary}");
            sb.AppendLine();
        }

        sb.AppendLine("> Structural pattern search blends semantic or lexical seeds with graph-based reranking. It favors reusable implementation shapes over isolated similar files.");
        return sb.ToString();
    }

    private async Task<IReadOnlyList<ImplementationPatternSeed>> CollectImplementationPatternSeedsAsync(
        string query,
        string? projectContext,
        bool excludeTests,
        CancellationToken cancellationToken)
    {
        var seeds = new Dictionary<string, ImplementationPatternSeed>(StringComparer.Ordinal);
        var lexicalMatches = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = query,
                ProjectContext = projectContext,
                Limit = 24
            },
            cancellationToken);

        foreach (var match in lexicalMatches)
            AddImplementationPatternSeed(seeds, match, 0.55, "lexical");

        if (await embeddingProvider.IsAvailableAsync(cancellationToken))
        {
            var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(query, cancellationToken);
            if (queryEmbedding is { Length: > 0 })
            {
                var semanticMatches = await codeGraph.FindImplementationPatternCandidatesAsync(
                    queryEmbedding,
                    projectContext,
                    excludeTests,
                    topK: 24,
                    cancellationToken);

                foreach (var (node, score) in semanticMatches)
                    AddImplementationPatternSeed(seeds, node, score, "embedding");
            }
        }

        return seeds.Values
            .Where(seed => IsImplementationPatternAnchor(seed.Node))
            .Where(seed => !excludeTests || !IsConfiguredTestNode(seed.Node))
            .Where(seed => AllowsProfile(seed.Node, AnalysisProfile.AgentContext))
            .OrderByDescending(seed => seed.Score)
            .ThenBy(seed => NodeDisplayRank(seed.Node))
            .ThenBy(seed => seed.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<ImplementationPatternCandidate?> BuildImplementationPatternCandidateAsync(
        ImplementationPatternSeed seed,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var contextTask = codeGraph.GetContextForEditingAsync(seed.Node.Id, cancellationToken);
        var impactTask = codeGraph.FindImpactAsync(seed.Node.Id, depth: 2, cancellationToken);
        var downstreamTask = codeGraph.FindDownstreamAsync(seed.Node.Id, depth: 3, cancellationToken);
        var relatedTestsTask = codeGraph.FindRelatedTestsAsync(seed.Node.Id, seed.Node.ProjectContext ?? projectContext, cancellationToken);
        await Task.WhenAll(contextTask, impactTask, downstreamTask, relatedTestsTask);

        var context = await contextTask;
        if (context.Node is null)
            return null;

        var impact = await impactTask;
        var downstream = await downstreamTask;
        var relatedTests = await relatedTestsTask;
        var directTests = relatedTests
            .Where(match => match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield))
            .DistinctBy(node => node.Id)
            .Take(3)
            .ToArray();
        var heuristicTests = relatedTests
            .Where(match => !match.MatchType.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Node)
            .Where(node => AllowsProfile(node, AnalysisProfile.TestShield))
            .DistinctBy(node => node.Id)
            .Take(2)
            .ToArray();

        var neighborhood = new[] { context.Node }
            .Concat(context.Callers)
            .Concat(context.Callees)
            .Concat(context.Interfaces)
            .Concat(impact.Select(item => item.Node))
            .Concat(downstream.Select(item => item.Node))
            .Where(node => AllowsProfile(node, AnalysisProfile.AgentContext))
            .DistinctBy(node => node.Id)
            .ToArray();

        var entryNodes = RankNodesForDisplay(neighborhood.Where(IsPatternEntryNode)).Take(3).ToArray();
        var behaviorNodes = RankNodesForDisplay(neighborhood.Where(IsPatternBehaviorNode)).Take(3).ToArray();
        var contractNodes = RankNodesForDisplay(neighborhood.Where(IsPatternContractNode)).Take(2).ToArray();
        var storeNodes = RankNodesForDisplay(neighborhood.Where(IsPatternStoreNode)).Take(3).ToArray();
        var externalNodes = RankNodesForDisplay(neighborhood.Where(IsPatternExternalBoundaryNode)).Take(3).ToArray();
        var testNodes = directTests.Concat(heuristicTests).DistinctBy(node => node.Id).ToArray();

        var bucketCount = 0;
        if (entryNodes.Length > 0) bucketCount++;
        if (behaviorNodes.Length > 0) bucketCount++;
        if (contractNodes.Length > 0) bucketCount++;
        if (storeNodes.Length > 0) bucketCount++;
        if (externalNodes.Length > 0) bucketCount++;
        if (testNodes.Length > 0) bucketCount++;

        var freshness = BuildFreshness(context.Node);
        var score = (int)Math.Round(seed.Score * 100, MidpointRounding.AwayFromZero)
                    + bucketCount * 18
                    + directTests.Length * 8
                    + heuristicTests.Length * 3
                    + (entryNodes.Length > 0 ? 6 : 0)
                    + (storeNodes.Length > 0 ? 6 : 0)
                    + (externalNodes.Length > 0 ? 6 : 0)
                    - (freshness.Confidence == "Low" ? 25 : freshness.Confidence == "Medium" ? 6 : 0);

        var confidence = DeterminePatternConfidence(bucketCount, entryNodes.Length, storeNodes.Length, externalNodes.Length, directTests.Length, freshness.Confidence);
        var shape = BuildPatternShape(entryNodes, behaviorNodes, contractNodes, storeNodes, externalNodes, testNodes);
        var scopeKey = ResolvePatternScopeKey(neighborhood, seed.Node);
        var displayName = ResolvePatternDisplayName(scopeKey, seed.Node, entryNodes, behaviorNodes, storeNodes);
        var evidenceSummary = BuildPatternEvidenceSummary(seed, entryNodes, behaviorNodes, contractNodes, storeNodes, externalNodes, directTests, heuristicTests);
        var primaryFile = ResolvePatternPrimaryFile(seed.Node, neighborhood);

        return new ImplementationPatternCandidate(
            scopeKey,
            displayName,
            shape,
            confidence,
            evidenceSummary,
            score,
            bucketCount,
            $"{string.Join(" + ", seed.RetrievalKinds.Order(StringComparer.OrdinalIgnoreCase))} ({seed.Score.ToString("F2", CultureInfo.InvariantCulture)})",
            primaryFile,
            entryNodes,
            behaviorNodes,
            contractNodes,
            storeNodes,
            externalNodes,
            testNodes);
    }

    private static void AddImplementationPatternSeed(
        IDictionary<string, ImplementationPatternSeed> seeds,
        CodeNode node,
        double score,
        string retrievalKind)
    {
        if (seeds.TryGetValue(node.Id, out var existing))
        {
            var mergedKinds = existing.RetrievalKinds
                .Concat([retrievalKind])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            seeds[node.Id] = existing with
            {
                Score = Math.Max(existing.Score, score),
                RetrievalKinds = mergedKinds
            };
            return;
        }

        seeds[node.Id] = new ImplementationPatternSeed(node, score, [retrievalKind]);
    }

    private static string DescribePatternSeedRetrieval(IReadOnlyCollection<ImplementationPatternSeed> seeds)
    {
        var retrievalKinds = seeds
            .SelectMany(seed => seed.RetrievalKinds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return retrievalKinds.Length switch
        {
            0 => "no retrieval sources",
            1 when retrievalKinds[0].Equals("embedding", StringComparison.OrdinalIgnoreCase) => "embedding seeds with structural reranking",
            1 => "lexical graph seeds with structural reranking",
            _ => "embedding and lexical graph seeds with structural reranking"
        };
    }

    private static bool IsImplementationPatternAnchor(CodeNode node) =>
        node.Type is CodeNodeType.ApiEndpoint or CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.File or CodeNodeType.ExternalConcept
        && !LooksLikeDocumentationPath(node.FilePath ?? string.Empty);

    private bool IsPatternEntryNode(CodeNode node) =>
        IsApiNode(node)
        || (IsFrontendFile(node) && !IsConfiguredTestNode(node))
        || TextMatches(node.Name, "Command")
        || TextMatches(node.Name, "Job")
        || TextMatches(node.Name, "Worker")
        || TextMatches(node.FilePath, "/Cli/")
        || TextMatches(node.FilePath, "\\Cli\\")
        || TextMatches(node.FilePath, "/Commands/")
        || TextMatches(node.FilePath, "\\Commands\\");

    private bool IsPatternBehaviorNode(CodeNode node) =>
        !IsConfiguredTestNode(node)
        && !IsPatternEntryNode(node)
        && !IsPatternContractNode(node)
        && !IsPatternStoreNode(node)
        && !IsPatternExternalBoundaryNode(node)
        && node.Type is CodeNodeType.Method or CodeNodeType.Class
        && (IsApplicationNode(node)
            || IsDomainNode(node)
            || TextMatches(node.Name, "Service")
            || TextMatches(node.Name, "Handler")
            || TextMatches(node.Name, "Workflow")
            || TextMatches(node.Name, "UseCase"));

    private bool IsPatternContractNode(CodeNode node) =>
        IsContractNode(node);

    private bool IsPatternStoreNode(CodeNode node) =>
        !IsConfiguredTestNode(node)
        && node.Type is CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.Method
        && (TextMatches(node.Name, "Repository")
            || TextMatches(node.Name, "Store")
            || TextMatches(node.Name, "Gateway")
            || TextMatches(node.Name, "Adapter")
            || TextMatches(node.Name, "Client")
            || TextMatches(node.FilePath, "Repository")
            || TextMatches(node.FilePath, "Store")
            || TextMatches(node.FilePath, "Gateway")
            || IsInfrastructureNode(node));

    private static bool IsPatternExternalBoundaryNode(CodeNode node)
    {
        if (node.Type is CodeNodeType.DatabaseTable or CodeNodeType.MessageTopic or CodeNodeType.ExternalService)
            return true;

        return node.Type == CodeNodeType.ExternalConcept
               && node.Properties.TryGetValue("externalKind", out var kind)
               && kind is "DatabaseOperation" or "ExternalService" or "MessageTopic";
    }

    private static string BuildPatternShape(
        IReadOnlyCollection<CodeNode> entryNodes,
        IReadOnlyCollection<CodeNode> behaviorNodes,
        IReadOnlyCollection<CodeNode> contractNodes,
        IReadOnlyCollection<CodeNode> storeNodes,
        IReadOnlyCollection<CodeNode> externalNodes,
        IReadOnlyCollection<CodeNode> testNodes)
    {
        var labels = new List<string>();
        if (entryNodes.Any(node => IsFrontendFile(node)))
            labels.Add("frontend entry");
        if (entryNodes.Any(node => !IsFrontendFile(node)))
            labels.Add("api/command entry");
        if (behaviorNodes.Count > 0)
            labels.Add("application/domain");
        if (contractNodes.Count > 0)
            labels.Add("contract");
        if (storeNodes.Count > 0)
            labels.Add("repository/store");
        if (externalNodes.Count > 0)
            labels.Add("database/event boundary");
        if (testNodes.Count > 0)
            labels.Add("tests");

        return labels.Count == 0 ? "related implementation slice" : string.Join(" -> ", labels);
    }

    private static string DeterminePatternConfidence(
        int bucketCount,
        int entryCount,
        int storeCount,
        int externalCount,
        int directTestCount,
        string freshnessConfidence) =>
        bucketCount >= 4 && entryCount > 0 && (storeCount > 0 || externalCount > 0) && directTestCount > 0 && freshnessConfidence != "Low"
            ? "exact structural"
            : bucketCount >= 3 && freshnessConfidence != "Low"
                ? "structural"
                : "related";

    private static string ResolvePatternScopeKey(IEnumerable<CodeNode> nodes, CodeNode fallback)
    {
        var namespaceKey = nodes
            .Select(node => node.Namespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
            {
                var parts = value!.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Length >= 3 ? string.Join(".", parts.Take(3)) : string.Join(".", parts);
            })
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(namespaceKey))
            return namespaceKey;

        var pathKey = nodes
            .Select(node => node.FilePath?.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                var parts = path!.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(part => !part.Equals("src", StringComparison.OrdinalIgnoreCase)
                                   && !part.Equals("tests", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return parts.Length >= 2 ? string.Join("/", parts.Take(2)) : string.Join("/", parts);
            })
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

        return !string.IsNullOrWhiteSpace(pathKey)
            ? pathKey
            : fallback.FilePath ?? fallback.Name;
    }

    private static string ResolvePatternDisplayName(
        string scopeKey,
        CodeNode seed,
        IReadOnlyCollection<CodeNode> entryNodes,
        IReadOnlyCollection<CodeNode> behaviorNodes,
        IReadOnlyCollection<CodeNode> storeNodes)
    {
        var scopeLeaf = scopeKey
            .Replace('\\', '/')
            .Split(['/', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (!string.IsNullOrWhiteSpace(scopeLeaf)
            && scopeLeaf.Length > 2
            && !scopeLeaf.Equals("Application", StringComparison.OrdinalIgnoreCase)
            && !scopeLeaf.Equals("Infrastructure", StringComparison.OrdinalIgnoreCase)
            && !scopeLeaf.Equals("Api", StringComparison.OrdinalIgnoreCase))
            return scopeLeaf;

        return entryNodes.Concat(behaviorNodes).Concat(storeNodes).Select(node => node.Name).FirstOrDefault()
               ?? seed.Name;
    }

    private static string BuildPatternEvidenceSummary(
        ImplementationPatternSeed seed,
        IReadOnlyCollection<CodeNode> entryNodes,
        IReadOnlyCollection<CodeNode> behaviorNodes,
        IReadOnlyCollection<CodeNode> contractNodes,
        IReadOnlyCollection<CodeNode> storeNodes,
        IReadOnlyCollection<CodeNode> externalNodes,
        IReadOnlyCollection<CodeNode> directTests,
        IReadOnlyCollection<CodeNode> heuristicTests)
    {
        var parts = new List<string>();
        if (entryNodes.Count > 0) parts.Add($"{entryNodes.Count} entry signal(s)");
        if (behaviorNodes.Count > 0) parts.Add($"{behaviorNodes.Count} behavior node(s)");
        if (contractNodes.Count > 0) parts.Add($"{contractNodes.Count} contract node(s)");
        if (storeNodes.Count > 0) parts.Add($"{storeNodes.Count} repository/store node(s)");
        if (externalNodes.Count > 0) parts.Add($"{externalNodes.Count} external boundary node(s)");
        if (directTests.Count > 0) parts.Add($"{directTests.Count} direct test(s)");
        else if (heuristicTests.Count > 0) parts.Add($"{heuristicTests.Count} heuristic test(s)");
        parts.Add($"{string.Join("+", seed.RetrievalKinds.Order(StringComparer.OrdinalIgnoreCase))} seed");
        return string.Join(", ", parts);
    }

    private static string ResolvePatternPrimaryFile(CodeNode seed, IReadOnlyCollection<CodeNode> nodes) =>
        nodes.Select(node => node.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => string.Equals(path, seed.FilePath, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
        ?? seed.FilePath
        ?? "unknown";

    private static string FormatPatternNodeList(IReadOnlyCollection<CodeNode> nodes) =>
        nodes.Count == 0
            ? "none found"
            : string.Join(", ", nodes.Take(4).Select(node => $"`{node.Name}`"));

    private sealed record ImplementationPatternSeed(
        CodeNode Node,
        double Score,
        IReadOnlyList<string> RetrievalKinds);

    private sealed record ImplementationPatternCandidate(
        string ScopeKey,
        string DisplayName,
        string Shape,
        string Confidence,
        string EvidenceSummary,
        int TotalScore,
        int StructuralBucketCount,
        string SeedSummary,
        string PrimaryFile,
        IReadOnlyList<CodeNode> EntryNodes,
        IReadOnlyList<CodeNode> BehaviorNodes,
        IReadOnlyList<CodeNode> ContractNodes,
        IReadOnlyList<CodeNode> StoreNodes,
        IReadOnlyList<CodeNode> ExternalBoundaryNodes,
        IReadOnlyList<CodeNode> TestNodes)
    {
        public int TestCount => TestNodes.Count;
    }
}
