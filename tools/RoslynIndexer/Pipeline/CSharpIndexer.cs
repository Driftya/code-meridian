using CodeMeridian.Sdk;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Orchestrates Roslyn indexing for .cs files.
/// </summary>
public sealed class CSharpIndexer(
    CodeMeridianClient client,
    IIndexedFileRoleClassifier fileRoleClassifier,
    ILogger<CSharpIndexer> logger)
{
    public CSharpIndexer(
        CodeMeridianClient client,
        ILogger<CSharpIndexer> logger)
        : this(
            client,
            new ConfiguredIndexedFileRoleClassifier(Microsoft.Extensions.Options.Options.Create(new CodebaseIndexingOptions())),
            logger)
    {
    }

    public async Task<IndexStats> IndexAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var configurationConstants = CSharpConfigurationConstantRegistry.Build(files);
        LogClassificationSummary(files, rootPath, projectContext, fileRoleClassifier, logger);

        foreach (var file in files)
        {
            try
            {
                ExtractFromFile(file, rootPath, projectContext, nodes, edges, configurationConstants);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        ApplyFileRoles(nodes, fileRoleClassifier);

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

    private static void ApplyFileRoles(List<IngestNodeRequest> nodes, IIndexedFileRoleClassifier fileRoleClassifier)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (string.IsNullOrWhiteSpace(node.FilePath))
                continue;

            var properties = node.Properties is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(node.Properties, StringComparer.Ordinal);
            properties["fileRole"] = fileRoleClassifier.Classify(node.FilePath).ToString();
            nodes[i] = node with { Properties = properties };
        }
    }

    private static void LogClassificationSummary(
        IEnumerable<FileInfo> files,
        string rootPath,
        string projectContext,
        IIndexedFileRoleClassifier fileRoleClassifier,
        ILogger logger)
    {
        var counts = Enum.GetValues<IndexedFileRole>().ToDictionary(role => role, _ => 0);
        var fileCount = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
            counts[fileRoleClassifier.Classify(relativePath)]++;
            fileCount++;
        }

        logger.LogInformation(
            "Classified {FileCount} indexed files for project {ProjectName}: Source={SourceCount}, Test={TestCount}, Migration={MigrationCount}, Snapshot={SnapshotCount}, Generated={GeneratedCount}, BuildArtifact={BuildArtifactCount}, Configuration={ConfigurationCount}, Unknown={UnknownCount}",
            fileCount,
            projectContext,
            counts[IndexedFileRole.Source],
            counts[IndexedFileRole.Test],
            counts[IndexedFileRole.Migration],
            counts[IndexedFileRole.Snapshot],
            counts[IndexedFileRole.Generated],
            counts[IndexedFileRole.BuildArtifact],
            counts[IndexedFileRole.Configuration],
            counts[IndexedFileRole.Unknown]);
    }

    private static void ExtractFromFile(
        FileInfo file,
        string rootPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        CSharpConfigurationConstantRegistry configurationConstants)
    {
        var source = File.ReadAllText(file.FullName);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, path: file.FullName);
        var root = tree.GetCompilationUnitRoot();

        var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        var walker = new CSharpAstWalker(relPath, projectContext, nodes, edges);
        walker.Visit(root);
        CSharpRouteExtractor.Extract(root, relPath, projectContext, nodes, edges);
        CSharpConfigurationUsageExtractor.Extract(root, relPath, projectContext, nodes, edges, configurationConstants);
    }
}
