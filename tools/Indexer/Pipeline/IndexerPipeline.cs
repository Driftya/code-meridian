using CodeMeridian.Indexer.Pipeline;
using CodeMeridian.Sdk;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.Indexer.Pipeline;

/// <summary>
/// Orchestrates the full index run:
///   1. Optionally clear existing project knowledge
///   2. Walk .cs files → Roslyn AST → code nodes + edges
///   3. Walk doc files → text documents
/// </summary>
public sealed class IndexerPipeline(
    CSharpIndexer csharpIndexer,
    DocumentIngester documentIngester,
    CodeMeridianClient client,
    ILogger<IndexerPipeline> logger)
{
    public async Task RunAsync(
        DirectoryInfo root,
        string projectContext,
        bool clear,
        bool includeDocs,
        CancellationToken cancellationToken = default)
    {
        if (!root.Exists)
        {
            logger.LogError("Directory does not exist: {Path}", root.FullName);
            return;
        }

        logger.LogInformation("=== CodeMeridian Indexer ===");
        logger.LogInformation("Project : {Project}", projectContext);
        logger.LogInformation("Root    : {Root}", root.FullName);

        if (clear)
        {
            logger.LogInformation("Clearing existing knowledge for '{Project}'...", projectContext);
            await client.ClearProjectKnowledgeAsync(projectContext, cancellationToken);
            logger.LogInformation("Cleared.");
        }

        // ── Phase 1: C# code graph ────────────────────────────────────────────
        var csFiles = root
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f.FullName))
            .ToArray();

        logger.LogInformation("Found {Count} C# files to index.", csFiles.Length);

        var stats = await csharpIndexer.IndexAsync(csFiles, projectContext, root.FullName, cancellationToken);

        logger.LogInformation(
            "Code graph: {Nodes} nodes, {Edges} edges ingested.",
            stats.Nodes, stats.Edges);

        // ── Phase 2: Documentation ────────────────────────────────────────────
        if (includeDocs)
        {
            var docFiles = root
                .EnumerateFiles("*.*", SearchOption.AllDirectories)
                .Where(f => IsDocFile(f.Name) && !IsGenerated(f.FullName))
                .ToArray();

            logger.LogInformation("Found {Count} documentation files to ingest.", docFiles.Length);

            var docStats = await documentIngester.IngestAsync(docFiles, projectContext, root.FullName, cancellationToken);

            logger.LogInformation("{Count} documents ingested.", docStats.Documents);
        }

        logger.LogInformation("=== Indexing complete for '{Project}' ===", projectContext);
    }

    private static bool IsGenerated(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
        path.Contains(".generated.") ||
        path.Contains("AssemblyInfo.cs") ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocFile(string name) =>
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        name is "agents.md" or "README.md" or "ARCHITECTURE.md" or "CHANGELOG.md";
}
