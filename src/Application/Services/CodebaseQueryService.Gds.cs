using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

// ── GDS (Graph Data Science) algorithm formatters ─────────────────────────────
// SRP: this file formats results from Neo4j GDS plugin algorithms only.
// Structural analytics live in CodebaseQueryService.Analytics.cs.
// Core CRUD methods live in CodebaseQueryService.cs.

public partial class CodebaseQueryService
{
    public async Task<string> GetPageRankAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
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
        sb.AppendLine($"## PageRank — Architectural Influence{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Nodes ranked by **transitive call-graph influence** (not just direct fan-in):\n");
        sb.AppendLine("| Rank | Score | Type | Name | File |");
        sb.AppendLine("|------|-------|------|------|------|");

        var rank = 1;
        foreach (var (node, score) in results
                     .OrderBy(item => NodeDisplayRank(item.Node))
                     .ThenByDescending(item => item.Score))
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {rank++} | {score.ToString("F4", CultureInfo.InvariantCulture)} | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> PageRank captures *who calls the callers* — deeper architectural weight than fan-in alone.");

        return sb.ToString();
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
        sb.AppendLine($"## Betweenness Centrality — Bridge Nodes{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Nodes that sit **between subsystems** — the connective tissue of your codebase:\n");
        sb.AppendLine("| Rank | Score | Type | Name | File |");
        sb.AppendLine("|------|-------|------|------|------|");

        var rank = 1;
        foreach (var (node, score) in results
                     .OrderBy(item => NodeDisplayRank(item.Node))
                     .ThenByDescending(item => item.Score))
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
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
        sb.AppendLine($"## Bridge Nodes{(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine("Small but structurally important nodes that appear to connect otherwise separate parts of the system:\n");
        sb.AppendLine("| Rank | Score | Type | Name | Connects | Risk note | Confidence | File |");
        sb.AppendLine("|------|-------|------|------|----------|-----------|------------|------|");

        var rank = 1;
        foreach (var (node, score) in ranked)
        {
            var context = await codeGraph.GetContextForEditingAsync(node.Id, cancellationToken);
            var freshness = BuildFreshness(node);
            var layers = GetConnectedLayers(node, context);
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
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
                   "The graph may have no edges — run the indexer first.";

        var communities = results
            .GroupBy(r => r.Community)
            .OrderByDescending(g => g.Count())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"## Natural Modules (Louvain){(projectContext is not null ? $" — {projectContext}" : "")}");
        sb.AppendLine($"**{communities.Count}** organic communities detected from {results.Count} nodes:\n");

        foreach (var community in communities.Take(15))
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
                var loc = node.FilePath is not null ? $" — `{node.FilePath}`" : "";
                sb.AppendLine($"- **{node.Type}** `{node.Name}`{loc}");
            }

            if (members.Count > 10)
                sb.AppendLine($"- *…and {members.Count - 10} more*");

            sb.AppendLine();
        }

        if (communities.Count > 15)
            sb.AppendLine($"*{communities.Count - 15} smaller communities omitted.*");

        sb.AppendLine("> Communities represent organic module boundaries. Compare with your folder structure to identify hidden coupling.");

        return sb.ToString();
    }

    public async Task<string> SuggestExtractionsAsync(
        string? projectContext = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
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
                   "The graph may have no communities yet — run the indexer first.";

        var hotspotScores = (await codeGraph.FindHotspotsAsync(projectContext, limit: 50, cancellationToken))
            .ToDictionary(item => item.Node.Id, item => item.FanIn, StringComparer.Ordinal);
        var godClassScores = (await codeGraph.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3, cancellationToken))
            .ToDictionary(item => item.Node.Id, item => (item.LineCount, item.FanIn), StringComparer.Ordinal);
        var coverageGapIds = (await codeGraph.FindCoverageGapsAsync(projectContext, cancellationToken))
            .Where(node => AllowsProfile(node, AnalysisProfile.CoverageGaps))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new List<ExtractionCandidate>();
        foreach (var community in communities
                     .GroupBy(item => item.Community)
                     .OrderByDescending(group => group.Count()))
        {
            var members = community
                .Select(item => item.Node)
                .Where(node => AllowsProfile(node, AnalysisProfile.DesignSmells) && !IsConfiguredTestNode(node))
                .Where(node => node.Type is CodeNodeType.Method or CodeNodeType.Class or CodeNodeType.Interface)
                .DistinctBy(node => node.Id)
                .ToArray();

            if (members.Length < 3)
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
            var score = ScoreExtractionCandidate(members.Length, uniqueFiles.Length, uniqueTests.Length, coverageGapCount, layers.Length, anchor, hotspotScores, godClassScores);
            var confidence = DescribeExtractionConfidence(uniqueTests.Length, coverageGapCount, layers.Length, anchor, hotspotScores, godClassScores);
            var reason = BuildExtractionReason(members, uniqueFiles.Length, uniqueTests.Length, coverageGapCount, layers, anchor, hotspotScores, godClassScores);

            candidates.Add(new ExtractionCandidate(
                community.Key,
                location,
                confidence,
                score,
                anchor,
                members,
                uniqueTests,
                coverageGapCount,
                reason));
        }

        var ranked = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Location, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 25))
            .ToArray();

        if (ranked.Length == 0)
            return $"No extraction candidates survived the current safety filters{(projectContext is not null ? $" in '{projectContext}'" : "")}. " +
                   "Try re-indexing, or wait until the graph contains larger production-only communities.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Refactor Extraction Candidates{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**{ranked.Length}** tightly connected groups ranked as extraction candidates.");
        sb.AppendLine();
        sb.AppendLine("| Rank | Move-from location | Confidence | Anchor | Nearby tests | Coverage gaps | Why |");
        sb.AppendLine("|---:|---|---|---|---|---:|---|");

        var rank = 1;
        foreach (var candidate in ranked)
        {
            var tests = candidate.NearbyTests.Count == 0
                ? "—"
                : string.Join("<br>", candidate.NearbyTests.Take(3).Select(test => $"`{test.Name}`"));
            sb.AppendLine(
                $"| {rank++} | `{candidate.Location}` | {candidate.Confidence} | `{candidate.Anchor.Name}` | {tests} | {candidate.CoverageGapCount} | {EscapeTableCell(candidate.Reason)} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Candidate details");
        foreach (var candidate in ranked)
        {
            sb.AppendLine($"#### Community {candidate.Community} - `{candidate.Location}`");
            sb.AppendLine($"- Anchor: `{candidate.Anchor.Name}` ({candidate.Anchor.Type})");
            sb.AppendLine($"- Members: {string.Join(", ", candidate.Members.Take(5).Select(member => $"`{member.Name}`"))}");
            sb.AppendLine($"- Nearby tests: {(candidate.NearbyTests.Count == 0 ? "none found" : string.Join(", ", candidate.NearbyTests.Take(4).Select(test => $"`{test.Name}`")))}");
            sb.AppendLine($"- Reason: {candidate.Reason}");
            sb.AppendLine();
        }

        sb.AppendLine("> Safe-first heuristic: candidates are dense natural modules with a strong internal anchor. Nearby tests and coverage gaps are included so you can judge whether an extraction is protected before changing boundaries.");
        return sb.ToString();
    }

    public async Task<string> FindSimilarToNodeAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await codeGraph.FindSimilarToNodeAsync(nodeId, projectContext, topK: 10, cancellationToken);

        if (results.Count == 0)
            return $"No similar nodes found for `{nodeId}`. " +
                   "Embeddings must be stored on nodes to use semantic similarity. " +
                   "Pass an `embeddingCsv` when calling ingest_code_node, or re-index with an embedding-enabled indexer.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Semantically Similar Nodes — `{nodeId}`");
        sb.AppendLine($"**{results.Count}** nodes with similar vector embeddings:\n");
        sb.AppendLine("| Similarity | Type | Name | File |");
        sb.AppendLine("|-----------|------|------|------|");

        foreach (var (node, score) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "—";
            sb.AppendLine($"| {(score * 100).ToString("F1", CultureInfo.InvariantCulture)}% | {node.Type} | `{node.Name}` | {file} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Semantic similarity finds conceptually related code regardless of call-graph proximity — useful for finding duplicates or related implementations.");

        return sb.ToString();
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
        sb.AppendLine($"## Hybrid Semantic Graph Search â€” `{query}`");
        sb.AppendLine($"**{results.Count}** results ranked by embedding similarity and graph proximity.");
        if (!string.IsNullOrWhiteSpace(nearNodeId))
            sb.AppendLine($"Anchored near `{nearNodeId}` within {Math.Clamp(maxHops, 1, 8)} hops.");
        sb.AppendLine();
        sb.AppendLine("| Similarity | Type | Name | File |");
        sb.AppendLine("|-----------|------|------|------|");

        foreach (var (node, score) in results)
        {
            var file = node.FilePath is not null ? $"`{node.FilePath}`" : "â€”";
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
        sb.AppendLine($"## Duplicate-Code Candidates{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**{results.Count}** similar method/class pairs across **{grouped.Count}** source groups.\n");
        sb.AppendLine("| Similarity | Type | Source | Candidate | Size | Refactor Risk | Coverage |");
        sb.AppendLine("|-----------|------|--------|-----------|------|---------------|----------|");

        foreach (var candidate in results)
        {
            var source = FormatDuplicateNode(candidate.Source);
            var duplicate = FormatDuplicateNode(candidate.Candidate);
            var size = $"{candidate.Source.LineCount ?? 0}/{candidate.Candidate.LineCount ?? 0} lines";
            var risk = FormatDuplicateRisk(candidate.SourceFanIn + candidate.CandidateFanIn);
            var coverage = FormatCoverage(candidate.SourceHasTestCoverage, candidate.CandidateHasTestCoverage);

            sb.AppendLine(
                $"| {(candidate.Score * 100).ToString("F1", CultureInfo.InvariantCulture)}% | " +
                $"{candidate.Source.Type} | {source} | {duplicate} | {size} | {risk} | {coverage} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Review these as candidates, not proof of duplication. Prioritise high-similarity pairs with low fan-in and some test coverage.");

        return sb.ToString();
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
        var namespaceLocation = members
            .Select(member => member.Namespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value =>
            {
                var parts = value!.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
            }, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Key;

        if (!string.IsNullOrWhiteSpace(namespaceLocation))
            return namespaceLocation;

        var pathLocation = members
            .Select(member => member.FilePath?.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .GroupBy(path =>
            {
                var parts = path!.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Length >= 3 ? $"{parts[0]}/{parts[1]}/{parts[2]}" : string.Join("/", parts);
            }, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Key;

        return pathLocation ?? "Unknown";
    }

    private static int ScoreExtractionCandidate(
        int memberCount,
        int fileCount,
        int testCount,
        int coverageGapCount,
        int layerCount,
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
        CodeNode anchor,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        if (testCount > 0 && coverageGapCount == 0 && layerCount <= 1 && (godClassScores.ContainsKey(anchor.Id) || hotspotScores.GetValueOrDefault(anchor.Id) >= 3))
            return "High";

        if (testCount > 0 && layerCount <= 2)
            return "Medium";

        return "Low";
    }

    private static string BuildExtractionReason(
        IReadOnlyCollection<CodeNode> members,
        int fileCount,
        int testCount,
        int coverageGapCount,
        IReadOnlyCollection<string> layers,
        CodeNode anchor,
        IReadOnlyDictionary<string, int> hotspotScores,
        IReadOnlyDictionary<string, (int LineCount, int FanIn)> godClassScores)
    {
        var parts = new List<string>
        {
            $"{members.Count} production members",
            $"{fileCount} files"
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
        string Reason);
}
