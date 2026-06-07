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
                value is not (CodeNodeType.Method or CodeNodeType.Class))
            {
                return $"Unknown duplicate candidate node type `{nodeType}`. Valid values: `Method`, `Class`.";
            }

            parsedType = value;
        }

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
}
