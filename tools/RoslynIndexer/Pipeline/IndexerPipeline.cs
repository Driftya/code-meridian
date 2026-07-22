using CodeMeridian.Sdk;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Orchestrates the full index run:
///   1. Optionally clear existing project knowledge
///   2. Walk .cs files ? Roslyn AST ? code nodes + edges
///   3. Walk doc files ? text documents
/// </summary>
public sealed class IndexerPipeline(
    CSharpIndexer csharpIndexer,
    CodeMeridianClient client,
    ILogger<IndexerPipeline> logger)
{
    public async Task RunAsync(
        DirectoryInfo root,
        string projectContext,
        bool clear,
        IReadOnlyCollection<string>? changedFiles = null,
        IReadOnlyCollection<string>? deletedFiles = null,
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

        foreach (var filePath in (deletedFiles ?? []).Where(IsCSharpSourcePath))
        {
            logger.LogInformation("Removing stale graph data for {File}...", filePath);
            await client.DeleteProjectFileAsync(projectContext, filePath, cancellationToken);
        }

        // -- Phase 1: C# code graph --------------------------------------------
        var allCsFiles = root
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f.FullName))
            .ToArray();
        var csFiles = allCsFiles
            .Where(f => changedFiles is null || changedFiles.Contains(RelativePath(root, f), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        logger.LogInformation(
            "Scanning {ScannedCount} C# files and ingesting {IngestedCount} file(s) in {Mode} mode.",
            allCsFiles.Length,
            csFiles.Length,
            changedFiles is null ? "full" : "incremental");

        if (changedFiles is not null)
        {
            foreach (var file in csFiles)
            {
                var relPath = RelativePath(root, file);
                logger.LogInformation("Removing stale graph data for {File} before re-indexing...", relPath);
                await client.DeleteProjectFileAsync(projectContext, relPath, cancellationToken);
            }
        }

        var stats = await csharpIndexer.IndexAsync(
            csFiles,
            projectContext,
            root.FullName,
            cancellationToken,
            allCsFiles,
            changedFiles is not null);

        logger.LogInformation(
            "Code graph: {Nodes} nodes and {Edges} edges ingested from {IngestedFiles} changed file(s) using {ScannedFiles} scan file(s).",
            stats.Nodes,
            stats.Edges,
            stats.IngestedFiles,
            stats.ScannedFiles);

        logger.LogInformation("=== Indexing complete for '{Project}' ===", projectContext);
    }

    private static bool IsGenerated(string path) =>
        HasIgnoredSegment(path) ||
        path.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCSharpSourcePath(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool HasIgnoredSegment(string path)
    {
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".meridian", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("build", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("coverage", StringComparison.OrdinalIgnoreCase));
    }

    private static string RelativePath(DirectoryInfo root, FileInfo file) =>
        Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
}
