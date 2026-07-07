using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

// -- GDS (Graph Data Science) algorithm formatters -----------------------------
// SRP: this file formats results from Neo4j GDS plugin algorithms only.
// Structural analytics live in CodebaseQueryService.Analytics.cs.
// Core CRUD methods live in CodebaseQueryService.cs.

public partial class CodebaseQueryService
{
    public async Task<string> GetPageRankAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        return await WithResolvedAnalysisOptionsAsync(projectContext, cancellationToken, async () =>
        {
            IReadOnlyList<(CodeNode Node, double Score)> results;
            try
            {
                results = await codeGraph.GetPageRankAsync(projectContext, limit: 20, cancellationToken);
            }
            catch (Exception ex)
            {
                return $"PageRank failed: {ex.Message}\n" +
                       "Ensure the Graph Data Science plugin is installed (`NEO4J_PLUGINS: '[\"graph-data-science\"]'` in docker-compose).";
            }

            if (results.Count == 0)
                return $"No results from PageRank{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                       "The graph may have no Calls/Uses/DependsOn edges yet.";

            var sb = new StringBuilder();
            sb.AppendLine($"## PageRank - Architectural Influence{(projectContext is not null ? $" - {projectContext}" : "")}");
            sb.AppendLine("Nodes ranked by **transitive call-graph influence** (not just direct fan-in). Production candidates are prioritized by default.\n");
            var sections = PartitionScoredNodesForDisplay(results.Select(item => (item.Node, item.Score)));
            AppendActionabilitySection(
                sb,
                "Production candidates",
                sections.ProductionCandidates,
                "Score",
                score => score.ToString("F4", CultureInfo.InvariantCulture));

            if (ShouldShowBroaderHeuristicMatchesInline())
            {
                AppendActionabilitySection(
                    sb,
                    "Broader heuristic matches",
                    sections.BroaderHeuristicMatches,
                    "Score",
                    score => score.ToString("F4", CultureInfo.InvariantCulture));
            }

            if (ShouldShowSuppressedNoiseInline())
            {
                AppendActionabilitySection(
                    sb,
                    "Suppressed noise",
                    sections.SuppressedNoise,
                    "Score",
                    score => score.ToString("F4", CultureInfo.InvariantCulture));
            }

            AppendSuppressedActionabilitySummary(sb, sections);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("> PageRank captures *who calls the callers* - deeper architectural weight than fan-in alone.");

            return sb.ToString();
        });
    }

    public async Task<string> GetBetweennessAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(CodeNode Node, double Score)> results;
        try
        {
            results = await codeGraph.GetBetweennessAsync(projectContext, limit: 20, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Betweenness Centrality failed: {ex.Message}\n" +
                   "Ensure the Graph Data Science plugin is installed.";
        }

        if (results.Count == 0)
            return $"No results from Betweenness Centrality{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "The graph may have no edges yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Betweenness Centrality - Bridge Nodes{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine("Nodes that sit **between subsystems** - the connective tissue of your codebase:\n");
        sb.AppendLine("| Rank | Score | Type | Name | File |");
        sb.AppendLine("|------|-------|------|------|------|");

        var rank = 1;
        foreach (var (node, score) in results
                     .OrderBy(item => NodeDisplayRank(item.Node))
                     .ThenByDescending(item => item.Score))
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "-";
            sb.AppendLine($"| {rank++} | {score.ToString("F0", CultureInfo.InvariantCulture)} | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Removing or changing a high-betweenness node disconnects large parts of the system. Handle with extreme care.");

        return sb.ToString();
    }

    private async Task<string> FindBridgesLegacyAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(CodeNode Node, double Score)> results;
        try
        {
            results = await codeGraph.GetBetweennessAsync(projectContext, limit: 12, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Bridge detection failed: {ex.Message}\n" +
                   "Ensure the Graph Data Science plugin is installed.";
        }

        if (results.Count == 0)
            return $"No bridge nodes detected{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "The graph may have no edges yet.";

        var ranked = results.OrderByDescending(item => item.Score).ToArray();
        var topScore = ranked[0].Score;
        var sb = new StringBuilder();
        sb.AppendLine($"## Bridge Nodes{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine("Small but structurally important nodes that appear to connect otherwise separate parts of the system:\n");
        sb.AppendLine("| Rank | Score | Type | Name | Connects | Risk note | Confidence | File |");
        sb.AppendLine("|------|-------|------|------|----------|-----------|------------|------|");

        var rank = 1;
        foreach (var (node, score) in ranked)
        {
            var context = await codeGraph.GetContextForEditingAsync(node.Id, cancellationToken);
            var freshness = BuildFreshness(node);
            var layers = GetConnectedLayers(node, context);
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "-";
            var connects = layers.Count > 0 ? string.Join(", ", layers.Take(4)) : "unknown";
            var risk = DescribeBridgeRisk(score, topScore, layers.Count, freshness.Confidence);
            sb.AppendLine($"| {rank++} | {score.ToString("F0", CultureInfo.InvariantCulture)} | {node.Type} | `{node.Name}` | {connects} | {risk} | {freshness.Confidence} | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Confidence reflects indexed metadata freshness, not mathematical certainty. Use `get_context_for_editing` and `find_impact` before changing a bridge node.");

        return sb.ToString();
    }

    public async Task<string> FindNaturalModulesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        return await WithResolvedAnalysisOptionsAsync(projectContext, cancellationToken, async () =>
        {
            IReadOnlyList<(CodeNode Node, long Community)> results;
            try
            {
                results = await codeGraph.FindNaturalModulesAsync(projectContext, cancellationToken);
            }
            catch (Exception ex)
            {
                return $"Community detection failed: {ex.Message}\n" +
                       "Ensure the Graph Data Science plugin is installed.";
            }

            if (results.Count == 0)
                return $"No communities detected{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                       "The graph may have no edges - run the indexer first.";

            var communities = ClassifyCommunities(results);

            var sb = new StringBuilder();
            sb.AppendLine($"## Natural Modules (Louvain){(projectContext is not null ? $" - {projectContext}" : "")}");
            sb.AppendLine($"**{communities.Count}** organic communities detected from {results.Count} nodes.\n");

            AppendCommunitySection(sb, "Production candidates", communities.Where(c => c.Bucket == ActionabilityBucket.ProductionCandidate).ToArray());
            if (ShouldShowBroaderHeuristicMatchesInline())
                AppendCommunitySection(sb, "Broader heuristic matches", communities.Where(c => c.Bucket == ActionabilityBucket.BroaderHeuristicMatch).ToArray());
            if (ShouldShowSuppressedNoiseInline())
                AppendCommunitySection(sb, "Suppressed noise", communities.Where(c => c.Bucket == ActionabilityBucket.SuppressedNoise).ToArray());
            AppendCommunitySuppressionSummary(sb, communities);
/*
        {
            var members = community.OrderBy(r => r.Node.Name).ToList();
            sb.AppendLine($"### Community {community.Key} ({members.Count} nodes)");

            // Infer a module name from the most common namespace segment
            var namespaces = members
                .Where(m => m.Node.Namespace is not null)
                .Select(m => m.Node.Namespace!.Split('.').LastOrDefault() ?? "")
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            if (namespaces is not null)
                sb.AppendLine($"*Dominant namespace segment: `{namespaces}`*");

            foreach (var (node, _) in members.Take(10))
            {
                var loc = node.FilePath is not null ? $" - `{node.FilePath}`" : "";
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{loc}");
            }

            if (members.Count > 10)
                sb.AppendLine($"- *...and {members.Count - 10} more*");

            sb.AppendLine();
        }

        if (communities.Count > 15)
            sb.AppendLine($"*{communities.Count - 15} smaller communities omitted.*");
*/

            sb.AppendLine("> Communities represent organic module boundaries. Compare with your folder structure to identify hidden coupling.");

            return sb.ToString();
        });
    }

    public async Task<string> SuggestExtractionsAsync(
        string? projectContext = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        return await WithResolvedAnalysisOptionsAsync(projectContext, cancellationToken, async () =>
        {
            IReadOnlyList<(CodeNode Node, long Community)> communities;
            try
            {
                communities = await codeGraph.FindNaturalModulesAsync(projectContext, cancellationToken);
            }
            catch (Exception ex)
            {
                return $"Extraction suggestion failed: {ex.Message}\n" +
                       "Ensure the Graph Data Science plugin is installed.";
            }

            if (communities.Count == 0)
                return $"No extraction candidates found{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                       "The graph may have no communities yet - run the indexer first.";

            var hotspotScores = (await codeGraph.FindHotspotsAsync(projectContext, limit: 50, cancellationToken))
                .ToDictionary(item => item.Node.Id, item => item.FanIn, StringComparer.Ordinal);
            var godClassScores = (await codeGraph.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3, cancellationToken))
                .ToDictionary(item => item.Node.Id, item => (item.LineCount, item.FanIn), StringComparer.Ordinal);
            var coverageGapIds = (await codeGraph.FindCoverageGapsAsync(projectContext, cancellationToken))
                .Where(node => AllowsProfile(node, AnalysisProfile.CoverageGaps))
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);

            var classifiedCommunities = ClassifyCommunities(communities);
            var primaryCandidates = new List<ExtractionCandidate>();
            var weakCandidates = new List<ExtractionCandidate>();
            foreach (var community in classifiedCommunities)
            {
                var members = community.Members
                    .Where(node => AllowsProfile(node, AnalysisProfile.DesignSmells) && !IsConfiguredTestNode(node))
                    .Where(node => node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface)
                    .DistinctBy(node => node.Id)
                    .ToArray();

                if (members.Length < analysisOptions.CommunityNoise.MinimumExtractionCandidateMembers)
                    continue;

                var tests = new List<CodeNode>();
                foreach (var member in members.Take(3))
                {
                    var relatedTests = await codeGraph.FindRelatedTestsAsync(member.Id, member.ProjectContext ?? projectContext, cancellationToken);
                    tests.AddRange(relatedTests
                        .Select(match => match.Node)
                        .Where(node => AllowsProfile(node, AnalysisProfile.TestShield) && IsConfiguredTestNode(node)));
                }

                var uniqueTests = tests.DistinctBy(node => node.Id).ToArray();
                var uniqueFiles = members
                    .Select(node => node.FilePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var coverageGapCount = members.Count(node => coverageGapIds.Contains(node.Id));
                var layers = members
                    .Select(InferLayer)
                    .Where(layer => !string.Equals(layer, "Unknown", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var anchor = SelectExtractionAnchor(members, hotspotScores, godClassScores);
                var location = ResolveExtractionLocation(members);
                var score = ScoreExtractionCandidate(members.Length, uniqueFiles.Length, uniqueTests.Length, coverageGapCount, layers.Length, community.ProductionRatio, anchor, hotspotScores, godClassScores);
                var confidence = DescribeExtractionConfidence(uniqueTests.Length, coverageGapCount, layers.Length, community.ProductionRatio, anchor, hotspotScores, godClassScores);
                var reason = BuildExtractionReason(members, uniqueFiles.Length, uniqueTests.Length, coverageGapCount, layers, community.ProductionRatio, anchor, hotspotScores, godClassScores);

                var candidate = new ExtractionCandidate(
                    community.CommunityId,
                    location,
                    confidence,
                    score,
                    anchor,
                    members,
                    uniqueTests,
                    coverageGapCount,
                    community.ProductionRatio,
                    reason);

                if (community.Bucket == ActionabilityBucket.ProductionCandidate
                    && score >= analysisOptions.CommunityNoise.MinimumPrimaryExtractionScore
                    && !string.Equals(confidence, "Low", StringComparison.OrdinalIgnoreCase))
                    primaryCandidates.Add(candidate);
                else
                    weakCandidates.Add(candidate);
            }

            var ranked = primaryCandidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Location, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 25))
                .ToArray();
            var weakRanked = weakCandidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Location, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 25))
                .ToArray();

            if (ranked.Length == 0 && weakRanked.Length == 0)
                return $"No extraction candidates survived the current safety filters{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                       "Try re-indexing, or wait until the graph contains larger production-only communities.";

            var sb = new StringBuilder();
            sb.AppendLine($"## Refactor Extraction Candidates{(projectContext is not null ? $" - {projectContext}" : "")}");
            sb.AppendLine($"**{ranked.Length}** primary extraction candidates survived the current actionability thresholds.");
            sb.AppendLine();
            AppendExtractionCandidateTable(sb, ranked, "Primary candidates");
            if (weakRanked.Length > 0 && ShouldShowBroaderHeuristicMatchesInline())
            {
                AppendExtractionCandidateTable(sb, weakRanked, "Weaker heuristic candidates");
            }
            else if (weakRanked.Length > 0)
            {
                sb.AppendLine($"> Hidden by default: {weakRanked.Length} weaker heuristic candidate{(weakRanked.Length == 1 ? string.Empty : "s")}. " +
                              "Set `CodeMeridian:Analysis:Ranking:ProductionOnlyByDefault=false` or enable `IncludeBroaderHeuristicMatches` to inspect them inline.");
                sb.AppendLine();
            }

/*
        var rank = 1;
        foreach (var candidate in ranked)
        {
            var tests = candidate.NearbyTests.Count == 0
                ? "-"
                : string.Join("<br>", candidate.NearbyTests.Take(3).Select(test => $"`{test.Name}`"));
            sb.AppendLine(
                $"| {rank++} | `{candidate.Location}` | {candidate.Confidence} | `{candidate.Anchor.Name}` | {tests} | {candidate.CoverageGapCount} | {EscapeTableCell(candidate.Reason)} |");
        }

        sb.AppendLine();
*/
            sb.AppendLine("### Candidate details");
            foreach (var candidate in ranked.Concat(ShouldShowBroaderHeuristicMatchesInline() ? weakRanked : Array.Empty<ExtractionCandidate>()))
            {
                sb.AppendLine($"#### Community {candidate.Community} - `{candidate.Location}`");
                sb.AppendLine($"- Anchor: `{candidate.Anchor.Name}` ({candidate.Anchor.Type})");
                sb.AppendLine($"- Production member ratio: {(candidate.ProductionRatio * 100).ToString("F0", CultureInfo.InvariantCulture)}%");
                sb.AppendLine($"- Members: {string.Join(", ", candidate.Members.Take(5).Select(member => $"`{member.Name}`"))}");
                sb.AppendLine($"- Nearby tests: {(candidate.NearbyTests.Count == 0 ? "none found" : string.Join(", ", candidate.NearbyTests.Take(4).Select(test => $"`{test.Name}`")))}");
                sb.AppendLine($"- Reason: {candidate.Reason}");
                sb.AppendLine();
            }

            sb.AppendLine("> Safe-first heuristic: candidates are dense natural modules with a strong internal anchor. Nearby tests and coverage gaps are included so you can judge whether an extraction is protected before changing boundaries.");
            return sb.ToString();
        });
    }

    public async Task<string> FindSimilarToNodeAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveAnalysisProjectContextForNodeAsync(nodeId, projectContext, cancellationToken);
        return await WithResolvedAnalysisOptionsAsync(projectContext, cancellationToken, async () =>
        {
            var results = await codeGraph.FindSimilarToNodeAsync(nodeId, projectContext, topK: 10, cancellationToken);

            if (results.Count == 0)
                return $"No similar nodes found for `{nodeId}`. " +
                       "Embeddings must be stored on nodes to use semantic similarity. " +
                       "Pass an `embeddingCsv` when calling ingest_code_node, or re-index with an embedding-enabled indexer.";

            var sb = new StringBuilder();
            sb.AppendLine($"## Semantically Similar Nodes - `{nodeId}`");
            sb.AppendLine($"**{results.Count}** nodes with similar vector embeddings:\n");

            var sections = PartitionSimilarityMatches(nodeId, results);
            AppendActionabilitySection(
                sb,
                "Same-family production matches",
                sections.ProductionCandidates,
                "Similarity",
                score => $"{(score * 100).ToString("F1", CultureInfo.InvariantCulture)}%");

            if (ShouldShowBroaderHeuristicMatchesInline())
            {
                AppendActionabilitySection(
                    sb,
                    "Broader semantic matches",
                    sections.BroaderHeuristicMatches,
                    "Similarity",
                    score => $"{(score * 100).ToString("F1", CultureInfo.InvariantCulture)}%");
            }

            if (ShouldShowSuppressedNoiseInline())
            {
                AppendActionabilitySection(
                    sb,
                    "Suppressed test/config matches",
                    sections.SuppressedNoise,
                    "Similarity",
                    score => $"{(score * 100).ToString("F1", CultureInfo.InvariantCulture)}%");
            }
            else
            {
                AppendSuppressedActionabilitySummary(sb, sections);
            }

            sb.AppendLine();
            sb.AppendLine("> Semantic similarity keeps same-family production matches first by default. Enable broader output to inspect cross-layer or test-adjacent semantic neighbors.");

            return sb.ToString();
        });
    }

    public async Task<string> FindHybridSearchAsync(
        string query,
        string? nearNodeId = null,
        int maxHops = 3,
        string? projectContext = null,
        bool excludeTests = true,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!await embeddingProvider.IsAvailableAsync(cancellationToken))
        {
            return "Hybrid search requires embeddings to be enabled. " +
                   "Set `Embedding__Enabled=true` and re-index code nodes with embeddings stored on them.";
        }

        var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(query, cancellationToken);
        if (queryEmbedding is null or { Length: 0 })
        {
            return "Hybrid search could not generate a query embedding. " +
                   "Check the embedding provider configuration and try again.";
        }

        var results = await codeGraph.FindHybridMatchesAsync(
            queryEmbedding,
            nearNodeId,
            Math.Clamp(maxHops, 1, 8),
            projectContext,
            excludeTests,
            Math.Clamp(limit, 1, 25),
            cancellationToken);

        if (results.Count == 0)
        {
            return "No hybrid-search results found. " +
                   "Try broadening the graph neighborhood, lowering filters, or indexing more embedded nodes.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Hybrid Semantic Graph Search - `{query}`");
        sb.AppendLine($"**{results.Count}** results ranked by embedding similarity and graph proximity.");
        if (!string.IsNullOrWhiteSpace(nearNodeId))
            sb.AppendLine($"Anchored near `{nearNodeId}` within {Math.Clamp(maxHops, 1, 8)} hops.");
        sb.AppendLine();
        sb.AppendLine("| Similarity | Type | Name | File |");
        sb.AppendLine("|-----------|------|------|------|");

        foreach (var (node, score) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "-";
            sb.AppendLine($"| {(score * 100).ToString("F1", CultureInfo.InvariantCulture)}% | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Hybrid search uses embeddings for relevance and graph distance for scope. Tests are excluded by default.");

        return sb.ToString();
    }

    public async Task<string> FindDuplicateCandidatesAsync(
        string? projectContext = null,
        string? namespaceFilter = null,
        string? nodeType = null,
        int minLineCount = 5,
        double minSimilarity = 0.88,
        bool excludeTests = true,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        CodeNodeType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(nodeType))
        {
            if (!Enum.TryParse<CodeNodeType>(nodeType, ignoreCase: true, out var value) ||
                value is not (CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.ExternalConcept))
            {
                return $"Unknown duplicate candidate node type `{nodeType}`. Valid values: `Method`, `Class`, `ExternalConcept`.";
            }

            parsedType = value;
        }

        if (parsedType == CodeNodeType.ExternalConcept)
            return await FindFrontendStyleDuplicateCandidatesAsync(projectContext, namespaceFilter, excludeTests, cancellationToken);

        minLineCount = Math.Max(0, minLineCount);
        minSimilarity = Math.Clamp(minSimilarity, 0.0, 1.0);
        return await WithResolvedAnalysisOptionsAsync(projectContext, cancellationToken, async () =>
        {
            var results = await codeGraph.FindDuplicateCandidatesAsync(
                projectContext,
                namespaceFilter,
                parsedType,
                minLineCount,
                minSimilarity,
                excludeTests,
                limit: 20,
                cancellationToken);
            results = results
                .Where(candidate =>
                    AllowsProfile(candidate.Source, AnalysisProfile.DuplicateDetection) &&
                    AllowsProfile(candidate.Candidate, AnalysisProfile.DuplicateDetection) &&
                    (!excludeTests || (ResolveFileRole(candidate.Source) != IndexedFileRole.Test && ResolveFileRole(candidate.Candidate) != IndexedFileRole.Test)))
                .ToArray();

            if (results.Count == 0)
            {
                return "No duplicate-code candidates found. " +
                       "Embeddings must be stored on method/class nodes, and the current filters may be too strict. " +
                       "Try lowering `minSimilarity`, lowering `minLineCount`, or re-indexing with backend embeddings enabled.";
            }

            var grouped = results
                .GroupBy(candidate => candidate.Source.Id)
                .OrderByDescending(group => group.Max(candidate => candidate.Score))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"## Duplicate-Code Candidates{(projectContext is not null ? $" - {projectContext}" : string.Empty)}");
            sb.AppendLine($"**{results.Count}** similar method/class pairs across **{grouped.Count}** source groups.\n");

            var sections = PartitionDuplicateCandidates(results);
            AppendDuplicateCandidateSection(sb, "Low-risk extraction candidates", sections.ProductionCandidates);

            if (ShouldShowBroaderHeuristicMatchesInline())
                AppendDuplicateCandidateSection(sb, "Broader incidental similarity", sections.BroaderHeuristicMatches);

            if (ShouldShowSuppressedNoiseInline())
                AppendDuplicateCandidateSection(sb, "Suppressed test/config similarity", sections.SuppressedNoise);
            else
                AppendSuppressedActionabilitySummary(sb, sections);

            sb.AppendLine();
            sb.AppendLine("> Review these as candidates, not proof of duplication. Prioritise low fan-in, same-layer pairs with strong similarity before considering broader semantic overlap.");

            return sb.ToString();
        });
    }

    private RankedNodeSections<double> PartitionSimilarityMatches(
        string nodeId,
        IReadOnlyList<(CodeNode Node, double Score)> results)
    {
        var referenceType = TryInferReferenceNodeType(nodeId);
        var referenceLayer = TryInferLayerFromNodeId(nodeId);
        var ranked = new List<ScoredNodeDisplayItem<double>>(results.Count);

        foreach (var (node, score) in results)
        {
            var baseAssessment = AssessActionability(node);
            if (baseAssessment.Bucket == ActionabilityBucket.SuppressedNoise)
            {
                ranked.Add(new ScoredNodeDisplayItem<double>(node, score, baseAssessment));
                continue;
            }

            var sameFamily = !analysisOptions.SimilarityNoise.PreferSameNodeFamily
                             || referenceType is null
                             || node.Type == referenceType.Value;
            var sameLayer = !analysisOptions.SimilarityNoise.PreferSameLayer
                            || referenceLayer is null
                            || string.Equals(InferLayer(node), referenceLayer, StringComparison.OrdinalIgnoreCase);
            var primary = sameFamily
                          && sameLayer
                          && score >= analysisOptions.SimilarityNoise.MinimumPrimarySimilarity
                          && baseAssessment.Bucket == ActionabilityBucket.ProductionCandidate;

            var confidence = primary
                ? baseAssessment.Confidence
                : baseAssessment.Bucket == ActionabilityBucket.ProductionCandidate ? "Medium" : "Low";
            var bucket = primary
                ? ActionabilityBucket.ProductionCandidate
                : ActionabilityBucket.BroaderHeuristicMatch;
            var penalty = baseAssessment.RankPenalty
                          + (sameFamily ? 0 : 15)
                          + (sameLayer ? 0 : 10)
                          + Math.Max(0, (int)Math.Round((analysisOptions.SimilarityNoise.MinimumPrimarySimilarity - score) * 100, MidpointRounding.AwayFromZero));
            var reason = primary
                ? "same-family production match"
                : BuildSimilarityReason(sameFamily, sameLayer, score, analysisOptions.SimilarityNoise.MinimumPrimarySimilarity);

            ranked.Add(new ScoredNodeDisplayItem<double>(
                node,
                score,
                new NodeActionabilityAssessment(bucket, confidence, penalty, reason)));
        }

        return new RankedNodeSections<double>(
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.ProductionCandidate)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.BroaderHeuristicMatch)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.SuppressedNoise)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray());
    }

    private RankedNodeSections<DuplicateCandidate> PartitionDuplicateCandidates(IReadOnlyList<DuplicateCandidate> candidates)
    {
        var ranked = new List<ScoredNodeDisplayItem<DuplicateCandidate>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var sourceAssessment = AssessActionability(candidate.Source);
            var duplicateAssessment = AssessActionability(candidate.Candidate);
            if (sourceAssessment.Bucket == ActionabilityBucket.SuppressedNoise
                || duplicateAssessment.Bucket == ActionabilityBucket.SuppressedNoise)
            {
                var suppressedPenalty = Math.Max(sourceAssessment.RankPenalty, duplicateAssessment.RankPenalty);
                ranked.Add(new ScoredNodeDisplayItem<DuplicateCandidate>(
                    candidate.Source,
                    candidate,
                    new NodeActionabilityAssessment(
                        ActionabilityBucket.SuppressedNoise,
                        "Low",
                        suppressedPenalty,
                        "test or non-production duplicate candidate")));
                continue;
            }

            var combinedFanIn = candidate.SourceFanIn + candidate.CandidateFanIn;
            var sameLayer = !analysisOptions.DuplicateNoise.PreferSameLayer
                            || string.Equals(InferLayer(candidate.Source), InferLayer(candidate.Candidate), StringComparison.OrdinalIgnoreCase);
            var primary = candidate.Score >= analysisOptions.DuplicateNoise.MinimumPrimarySimilarity
                          && combinedFanIn <= analysisOptions.DuplicateNoise.MaximumPrimaryCombinedFanIn
                          && sameLayer
                          && sourceAssessment.Bucket == ActionabilityBucket.ProductionCandidate
                          && duplicateAssessment.Bucket == ActionabilityBucket.ProductionCandidate;

            var bucket = primary ? ActionabilityBucket.ProductionCandidate : ActionabilityBucket.BroaderHeuristicMatch;
            var confidence = primary
                ? (candidate.SourceHasTestCoverage || candidate.CandidateHasTestCoverage ? "High" : "Medium")
                : "Medium";
            var penalty = Math.Max(sourceAssessment.RankPenalty, duplicateAssessment.RankPenalty)
                          + Math.Max(0, combinedFanIn - analysisOptions.DuplicateNoise.MaximumPrimaryCombinedFanIn) * 5
                          + (sameLayer ? 0 : 10)
                          + Math.Max(0, (int)Math.Round((analysisOptions.DuplicateNoise.MinimumPrimarySimilarity - candidate.Score) * 100, MidpointRounding.AwayFromZero));
            var reason = primary
                ? "same-layer duplicate family with low refactor risk"
                : BuildDuplicateReason(candidate, combinedFanIn, sameLayer, analysisOptions.DuplicateNoise.MinimumPrimarySimilarity, analysisOptions.DuplicateNoise.MaximumPrimaryCombinedFanIn);

            ranked.Add(new ScoredNodeDisplayItem<DuplicateCandidate>(
                candidate.Source,
                candidate,
                new NodeActionabilityAssessment(bucket, confidence, penalty, reason)));
        }

        return new RankedNodeSections<DuplicateCandidate>(
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.ProductionCandidate)
                .OrderByDescending(item => item.Value.Score)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.BroaderHeuristicMatch)
                .OrderByDescending(item => item.Value.Score)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.SuppressedNoise)
                .OrderByDescending(item => item.Value.Score)
                .ThenBy(item => item.Assessment.RankPenalty)
                .ToArray());
    }

    private void AppendDuplicateCandidateSection(
        StringBuilder sb,
        string title,
        IReadOnlyList<ScoredNodeDisplayItem<DuplicateCandidate>> items)
    {
        sb.AppendLine($"### {title} ({items.Count})");
        if (items.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        foreach (var group in items
                     .GroupBy(item => item.Value.Source.Id)
                     .OrderByDescending(group => group.Max(item => item.Value.Score))
                     .ThenBy(group => group.First().Value.Source.Name, StringComparer.OrdinalIgnoreCase))
        {
            var source = group.First().Value.Source;
            sb.AppendLine($"#### `{source.Name}`");
            sb.AppendLine($"- Source: {FormatDuplicateNode(source)}");
            sb.AppendLine($"- Family size: {group.Count()} candidate{(group.Count() == 1 ? string.Empty : "s")}");
            sb.AppendLine();
            sb.AppendLine("| Similarity | Candidate | Size | Refactor Risk | Coverage | Confidence |");
            sb.AppendLine("|-----------|-----------|------|---------------|----------|------------|");

            foreach (var item in group.OrderByDescending(entry => entry.Value.Score).ThenBy(entry => entry.Value.Candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = item.Value;
                var duplicate = FormatDuplicateNode(candidate.Candidate);
                var size = $"{candidate.Source.LineCount ?? 0}/{candidate.Candidate.LineCount ?? 0} lines";
                var risk = FormatDuplicateRisk(candidate.SourceFanIn + candidate.CandidateFanIn);
                var coverage = FormatCoverage(candidate.SourceHasTestCoverage, candidate.CandidateHasTestCoverage);
                sb.AppendLine(
                    $"| {(candidate.Score * 100).ToString("F1", CultureInfo.InvariantCulture)}% | {duplicate} | {size} | {risk} | {coverage} | {item.Assessment.Confidence} |");
            }

            sb.AppendLine();
        }
    }
    private static string FormatDuplicateNode(CodeNode node)
    {
        var location = node.FilePath is null
            ? ""
            : $"<br>`{node.FilePath}{(node.LineNumber is not null ? $":{node.LineNumber}" : "")}`";

        return $"`{node.Name}`{location}";
    }

    private static string FormatDuplicateRisk(int fanIn) =>
        fanIn switch
        {
            >= 10 => $"High ({fanIn} callers)",
            >= 3 => $"Medium ({fanIn} callers)",
            _ => $"Low ({fanIn} callers)"
        };

    private static string FormatCoverage(bool sourceCovered, bool candidateCovered) =>
        (sourceCovered, candidateCovered) switch
        {
            (true, true) => "both covered",
            (true, false) => "source only",
            (false, true) => "candidate only",
            _ => "no direct test callers"
        };

    private static IReadOnlyList<string> GetConnectedLayers(CodeNode node, EditingContext context)
    {
        return new[] { node }
            .Concat(context.Callers)
            .Concat(context.Callees)
            .Select(InferLayer)
            .Where(layer => !string.Equals(layer, "Unknown", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(layer => layer, StringComparer.Ordinal)
            .ToArray();
    }

    private static string InferLayer(CodeNode node)
    {
        var namespaceValue = node.Namespace ?? string.Empty;
        var filePath = node.FilePath?.Replace('\\', '/') ?? string.Empty;

        if (ContainsSegment(namespaceValue, "Test") || filePath.Contains("/tests/", StringComparison.OrdinalIgnoreCase))
            return "Tests";

        if (ContainsSegment(namespaceValue, "Core") || filePath.Contains("/Core/", StringComparison.OrdinalIgnoreCase))
            return "Core";

        if (ContainsSegment(namespaceValue, "Application") || filePath.Contains("/Application/", StringComparison.OrdinalIgnoreCase))
            return "Application";

        if (ContainsSegment(namespaceValue, "Infrastructure") || filePath.Contains("/Infrastructure/", StringComparison.OrdinalIgnoreCase))
            return "Infrastructure";

        if (ContainsSegment(namespaceValue, "Api")
            || ContainsSegment(namespaceValue, "McpServer")
            || filePath.Contains("/Api/", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("/McpServer/", StringComparison.OrdinalIgnoreCase))
            return "API";

        return "Unknown";
    }

    private static CodeNodeType? TryInferReferenceNodeType(string nodeId)
    {
        foreach (var type in Enum.GetValues<CodeNodeType>())
        {
            if (nodeId.Contains($"::{type}::", StringComparison.OrdinalIgnoreCase)
                || nodeId.StartsWith(type + ":", StringComparison.OrdinalIgnoreCase))
                return type;
        }

        return null;
    }

    private static string? TryInferLayerFromNodeId(string nodeId)
    {
        if (nodeId.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("tests/", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("tests\\", StringComparison.OrdinalIgnoreCase))
            return "Tests";
        if (nodeId.Contains(".Core.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("/Core/", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("\\Core\\", StringComparison.OrdinalIgnoreCase))
            return "Core";
        if (nodeId.Contains(".Application.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("/Application/", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("\\Application\\", StringComparison.OrdinalIgnoreCase))
            return "Application";
        if (nodeId.Contains(".Infrastructure.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("/Infrastructure/", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("\\Infrastructure\\", StringComparison.OrdinalIgnoreCase))
            return "Infrastructure";
        if (nodeId.Contains(".Api.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains(".McpServer.", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("/Api/", StringComparison.OrdinalIgnoreCase)
            || nodeId.Contains("\\Api\\", StringComparison.OrdinalIgnoreCase))
            return "API";

        return null;
    }

    private static string BuildSimilarityReason(bool sameFamily, bool sameLayer, double score, double minimumPrimarySimilarity)
    {
        var parts = new List<string>();
        if (!sameFamily)
            parts.Add("cross-family match");
        if (!sameLayer)
            parts.Add("cross-layer match");
        if (score < minimumPrimarySimilarity)
            parts.Add("below primary similarity threshold");

        return parts.Count == 0 ? "broader semantic match" : string.Join(", ", parts);
    }

    private static string BuildDuplicateReason(
        DuplicateCandidate candidate,
        int combinedFanIn,
        bool sameLayer,
        double minimumPrimarySimilarity,
        int maximumPrimaryCombinedFanIn)
    {
        var parts = new List<string>();
        if (candidate.Score < minimumPrimarySimilarity)
            parts.Add("below primary similarity threshold");
        if (combinedFanIn > maximumPrimaryCombinedFanIn)
            parts.Add("higher caller count");
        if (!sameLayer)
            parts.Add("cross-layer pair");

        return parts.Count == 0 ? "broader duplicate candidate" : string.Join(", ", parts);
    }
    private static bool ContainsSegment(string value, string segment)
    {
        return value.Equals(segment, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + ".", StringComparison.OrdinalIgnoreCase)
               || value.EndsWith("." + segment, StringComparison.OrdinalIgnoreCase)
               || value.Contains("." + segment + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeBridgeRisk(double score, double topScore, int connectedLayerCount, string confidence)
    {
        if (confidence == "Low")
            return "high bridge score, but stale metadata lowers trust";

        if (connectedLayerCount >= 3)
            return "high bridge risk across multiple layers";

        if (score >= topScore * 0.7)
            return "high bridge risk on common execution paths";

        return "moderate bridge risk; validate with nearby callers";
    }

    private static CodeNode SelectExtractionAnchor(
        IReadOnlyCollection<CodeNode> members,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        return members
            .OrderByDescending(member => godClassScores.ContainsKey(member.Id))
            .ThenByDescending(member => hotspotScores.GetValueOrDefault(member.Id))
            .ThenByDescending(member => member.LineCount ?? 0)
            .ThenByDescending(member => member.ChangeCount ?? 0)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string ResolveExtractionLocation(IReadOnlyCollection<CodeNode> members)
    {
        var namespacePrefix = GetCommonNamespacePrefix(members);
        var pathPrefix = GetCommonDirectoryPrefix(members);

        if (!string.IsNullOrWhiteSpace(namespacePrefix))
        {
            var namespaceDepth = namespacePrefix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            var pathDepth = string.IsNullOrWhiteSpace(pathPrefix)
                ? 0
                : pathPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

            if (namespaceDepth >= 3 || namespaceDepth >= pathDepth)
                return namespacePrefix;
        }

        if (!string.IsNullOrWhiteSpace(pathPrefix))
            return pathPrefix;

        return namespacePrefix ?? "Unknown";
    }

    private static string? GetCommonNamespacePrefix(IReadOnlyCollection<CodeNode> members)
    {
        var namespaces = members
            .Select(member => member.Namespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

        if (namespaces.Length == 0)
            return null;

        return JoinCommonPrefix(namespaces, ".");
    }

    private static string? GetCommonDirectoryPrefix(IReadOnlyCollection<CodeNode> members)
    {
        var paths = members
            .Select(member => member.FilePath?.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Reverse()
                .Skip(1)
                .Reverse()
                .ToArray())
            .Where(parts => parts.Length > 0)
            .ToArray();

        if (paths.Length == 0)
            return null;

        return JoinCommonPrefix(paths, "/");
    }

    private static string? JoinCommonPrefix(IReadOnlyList<string[]> values, string separator)
    {
        if (values.Count == 0)
            return null;

        var minLength = values.Min(parts => parts.Length);
        var prefixLength = 0;
        for (var index = 0; index < minLength; index++)
        {
            var segment = values[0][index];
            if (values.Any(parts => !segment.Equals(parts[index], StringComparison.OrdinalIgnoreCase)))
                break;

            prefixLength++;
        }

        return prefixLength == 0
            ? null
            : string.Join(separator, values[0].Take(prefixLength));
    }

    private static int ScoreExtractionCandidate(
        int memberCount,
        int fileCount,
        int testCount,
        int coverageGapCount,
        int layerCount,
        double productionRatio,
        CodeNode anchor,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        var score = 0;

        score += memberCount switch
        {
            >= 4 and <= 8 => 6,
            3 => 4,
            <= 12 => 3,
            _ => 1
        };

        score += productionRatio >= 0.8d ? 3
            : productionRatio >= 0.6d ? 2
            : 0;
        score += fileCount <= 2 ? 3
            : fileCount <= 4 ? 2
            : 0;
        score += testCount > 0 ? 2 : 0;
        score -= coverageGapCount;
        score -= Math.Max(0, layerCount - 1) * 2;
        score += hotspotScores.GetValueOrDefault(anchor.Id) >= 3 ? 2 : 0;
        score += godClassScores.ContainsKey(anchor.Id) ? 3 : 0;

        return score;
    }

    private static string DescribeExtractionConfidence(
        int testCount,
        int coverageGapCount,
        int layerCount,
        double productionRatio,
        CodeNode anchor,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        if (productionRatio >= 0.8d
            && testCount > 0
            && coverageGapCount == 0
            && layerCount <= 1
            && (godClassScores.ContainsKey(anchor.Id) || hotspotScores.GetValueOrDefault(anchor.Id) >= 3))
            return "High";

        if (productionRatio >= 0.6d && testCount > 0 && layerCount <= 2)
            return "Medium";

        return "Low";
    }

    private static string BuildExtractionReason(
        IReadOnlyCollection<CodeNode> members,
        int fileCount,
        int testCount,
        int coverageGapCount,
        IReadOnlyCollection<string> layers,
        double productionRatio,
        CodeNode anchor,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        var parts = new List<string>
        {
            $"{members.Count} production members",
            $"{fileCount} files",
            $"{(productionRatio * 100).ToString("F0", CultureInfo.InvariantCulture)}% production density"
        };

        if (layers.Count > 0)
            parts.Add($"{layers.Count} layer{(layers.Count == 1 ? string.Empty : "s")}");
        if (hotspotScores.TryGetValue(anchor.Id, out var fanIn) && fanIn > 0)
            parts.Add($"anchor fan-in {fanIn}");
        if (godClassScores.TryGetValue(anchor.Id, out var godClass))
            parts.Add($"anchor is large ({godClass.LineCount} lines)");
        parts.Add(testCount == 0 ? "no nearby tests" : $"{testCount} nearby tests");
        if (coverageGapCount > 0)
            parts.Add($"{coverageGapCount} coverage gaps");

        return string.Join(", ", parts);
    }

    private sealed record ExtractionCandidate(
        long Community,
        string Location,
        string Confidence,
        int Score,
        CodeNode Anchor,
        IReadOnlyList<CodeNode> Members,
        IReadOnlyList<CodeNode> NearbyTests,
        int CoverageGapCount,
        double ProductionRatio,
        string Reason);

    private IReadOnlyList<CommunityCluster> ClassifyCommunities(IReadOnlyList<(CodeNode Node, long Community)> results)
    {
        return results
            .GroupBy(item => item.Community)
            .Select(group =>
            {
                var members = group
                    .Select(item => item.Node)
                    .DistinctBy(node => node.Id)
                    .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var productionMembers = members
                    .Where(node => AssessActionability(node).Bucket == ActionabilityBucket.ProductionCandidate)
                    .ToArray();
                var testMembers = members.Count(IsConfiguredTestNode);
                var suppressedMembers = members.Count(node => AssessActionability(node).Bucket == ActionabilityBucket.SuppressedNoise);
                var productionRatio = members.Length == 0 ? 0d : (double)productionMembers.Length / members.Length;
                var dominantNamespace = members
                    .Where(node => node.Namespace is not null)
                    .Select(node => node.Namespace!.Split('.').LastOrDefault() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .GroupBy(value => value)
                    .OrderByDescending(bucket => bucket.Count())
                    .FirstOrDefault()?.Key;
                var bucket = ClassifyCommunityBucket(members.Length, productionMembers.Length, productionRatio, testMembers);
                var confidence = bucket == ActionabilityBucket.ProductionCandidate
                    ? (productionRatio >= 0.8d ? "High" : "Medium")
                    : bucket == ActionabilityBucket.BroaderHeuristicMatch ? "Medium" : "Low";
                var reason = DescribeCommunityBucket(members.Length, productionMembers.Length, productionRatio, testMembers, suppressedMembers, bucket);

                return new CommunityCluster(group.Key, members, productionRatio, bucket, confidence, reason, dominantNamespace);
            })
            .OrderByDescending(cluster => cluster.Members.Count)
            .ThenByDescending(cluster => cluster.ProductionRatio)
            .ThenBy(cluster => cluster.CommunityId)
            .ToArray();
    }

    private ActionabilityBucket ClassifyCommunityBucket(
        int memberCount,
        int productionCount,
        double productionRatio,
        int testCount)
    {
        if (memberCount >= analysisOptions.CommunityNoise.MinimumCommunitySize
            && productionCount >= analysisOptions.CommunityNoise.MinimumCommunitySize
            && productionRatio >= analysisOptions.CommunityNoise.MinimumProductionMemberRatio)
            return ActionabilityBucket.ProductionCandidate;

        if (productionCount > 0 || (analysisOptions.CommunityNoise.IncludeTestCommunities && testCount > 0))
            return ActionabilityBucket.BroaderHeuristicMatch;

        return ActionabilityBucket.SuppressedNoise;
    }

    private static string DescribeCommunityBucket(
        int memberCount,
        int productionCount,
        double productionRatio,
        int testCount,
        int suppressedCount,
        ActionabilityBucket bucket)
    {
        return bucket switch
        {
            ActionabilityBucket.ProductionCandidate => $"{productionCount}/{memberCount} actionable production members",
            ActionabilityBucket.BroaderHeuristicMatch when memberCount < 3 => "micro-community below the primary size threshold",
            ActionabilityBucket.BroaderHeuristicMatch when productionRatio < 0.6d => $"{(productionRatio * 100).ToString("F0", CultureInfo.InvariantCulture)}% production density",
            ActionabilityBucket.BroaderHeuristicMatch => "partially actionable cluster with weak production density",
            _ when testCount > 0 && testCount == memberCount => "test-only community",
            _ when suppressedCount == memberCount => "config/generated/noise-only community",
            _ => "low-actionability community"
        };
    }

    private void AppendCommunitySection(StringBuilder sb, string title, IReadOnlyList<CommunityCluster> communities)
    {
        sb.AppendLine($"### {title} ({communities.Count})");
        if (communities.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        foreach (var community in communities.Take(analysisOptions.CommunityNoise.MaximumVisibleCommunities))
        {
            sb.AppendLine($"#### Community {community.CommunityId} ({community.Members.Count} nodes)");
            if (!string.IsNullOrWhiteSpace(community.DominantNamespace))
                sb.AppendLine($"- Dominant namespace segment: `{community.DominantNamespace}`");
            sb.AppendLine($"- Confidence: {community.Confidence}");
            sb.AppendLine($"- Reason: {community.Reason}");
            sb.AppendLine($"- Production density: {(community.ProductionRatio * 100).ToString("F0", CultureInfo.InvariantCulture)}%");
            foreach (var node in community.Members.Take(6))
            {
                var loc = node.FilePath is not null ? $" - `{node.FilePath}`" : string.Empty;
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{loc}");
            }

            if (community.Members.Count > 6)
                sb.AppendLine($"- ...and {community.Members.Count - 6} more");

            sb.AppendLine();
        }
    }

    private void AppendCommunitySuppressionSummary(StringBuilder sb, IReadOnlyList<CommunityCluster> communities)
    {
        var broaderCount = communities.Count(cluster => cluster.Bucket == ActionabilityBucket.BroaderHeuristicMatch);
        var suppressedCount = communities.Count(cluster => cluster.Bucket == ActionabilityBucket.SuppressedNoise);
        if (broaderCount == 0 && suppressedCount == 0)
            return;

        var singletonCount = communities.Count(cluster => cluster.Members.Count < analysisOptions.CommunityNoise.MinimumCommunitySize);
        var testOnlyCount = communities.Count(cluster =>
            cluster.Bucket == ActionabilityBucket.SuppressedNoise &&
            cluster.Members.All(IsConfiguredTestNode));
        sb.AppendLine(
            $"> Hidden by default: {broaderCount} broader heuristic communit{(broaderCount == 1 ? "y" : "ies")}, " +
            $"{suppressedCount} suppressed noise communit{(suppressedCount == 1 ? "y" : "ies")} ({singletonCount} micro/singleton, {testOnlyCount} test-only).");
        sb.AppendLine();
    }

    private static void AppendExtractionCandidateTable(StringBuilder sb, IReadOnlyList<ExtractionCandidate> candidates, string title)
    {
        sb.AppendLine($"### {title} ({candidates.Count})");
        if (candidates.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Rank | Move-from location | Confidence | Anchor | Production density | Nearby tests | Coverage gaps | Why |");
        sb.AppendLine("|---:|---|---|---|---:|---|---:|---|");

        var rank = 1;
        foreach (var candidate in candidates)
        {
            var tests = candidate.NearbyTests.Count == 0
                ? "-"
                : string.Join("<br>", candidate.NearbyTests.Take(3).Select(test => $"`{test.Name}`"));
            sb.AppendLine(
                $"| {rank++} | `{candidate.Location}` | {candidate.Confidence} | `{candidate.Anchor.Name}` | {(candidate.ProductionRatio * 100).ToString("F0", CultureInfo.InvariantCulture)}% | {tests} | {candidate.CoverageGapCount} | {EscapeTableCell(candidate.Reason)} |");
        }

        sb.AppendLine();
    }

    private sealed record CommunityCluster(
        long CommunityId,
        IReadOnlyList<CodeNode> Members,
        double ProductionRatio,
        ActionabilityBucket Bucket,
        string Confidence,
        string Reason,
        string? DominantNamespace);
}



