using CodeMeridian.Sdk;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Orchestrates Roslyn indexing for .cs files.
/// </summary>
public sealed class CSharpIndexer(
    CodeMeridianClient client,
    ILogger<CSharpIndexer> logger)
{
    public async Task<IndexStats> IndexAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();

        foreach (var file in files)
        {
            try
            {
                ExtractFromFile(file, rootPath, projectContext, nodes, edges);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        var attemptedCallEdges = edges.Count(e => e.RelationshipType == "Calls");
        var attemptedReferenceEdges = edges.Count(e => e.RelationshipType is "Uses" or "Implements" or "Inherits");
        edges = CSharpCallEdgeResolver.Resolve(nodes, edges);
        edges = CSharpReferenceEdgeResolver.Resolve(nodes, edges);
        var resolvedCallEdges = edges.Count(e => e.RelationshipType == "Calls");
        var resolvedReferenceEdges = edges.Count(e => e.RelationshipType is "Uses" or "Implements" or "Inherits");
        if (attemptedCallEdges > 0)
        {
            logger.LogInformation(
                "  Resolved {Resolved}/{Attempted} local call edge(s).",
                resolvedCallEdges,
                attemptedCallEdges);
        }
        if (attemptedReferenceEdges > 0)
        {
            logger.LogInformation(
                "  Resolved {Resolved}/{Attempted} local type reference edge(s).",
                resolvedReferenceEdges,
                attemptedReferenceEdges);
        }

        await CSharpBatchIngestionWriter.BatchIngestNodesAsync(client, logger, nodes, projectContext, cancellationToken);
        await CSharpBatchIngestionWriter.BatchIngestEdgesAsync(client, logger, edges, cancellationToken);

        return new IndexStats(nodes.Count, edges.Count);
    }

    private static void ExtractFromFile(
        FileInfo file,
        string rootPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges)
    {
        var source = File.ReadAllText(file.FullName);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, path: file.FullName);
        var root = tree.GetCompilationUnitRoot();

        var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        var walker = new CSharpAstWalker(relPath, projectContext, nodes, edges);
        walker.Visit(root);
        CSharpRouteExtractor.Extract(root, relPath, projectContext, nodes, edges);
        CSharpConfigurationUsageExtractor.Extract(root, relPath, projectContext, nodes, edges);
    }
}
