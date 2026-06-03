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
        foreach (var (node, score) in results)
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
        foreach (var (node, score) in results)
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
}
