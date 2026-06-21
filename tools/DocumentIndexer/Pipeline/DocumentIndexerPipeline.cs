using CodeMeridian.Sdk;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.DocumentIndexer.Pipeline;

public sealed partial class DocumentIndexerPipeline(CodeMeridianClient client, ILogger<DocumentIndexerPipeline> logger)
{
    public async Task<DocumentStats> IngestAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        var mcpToolFiles = DocumentMcpToolReferenceExtractor.BuildMcpToolFileMap(rootPath, projectContext);

        foreach (var file in files)
        {
            try
            {
                var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
                var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var chunks = DocumentTextSplitter.SplitIntoChunks(content);
                var relatedDocuments = DocumentReferenceExtractor.ExtractDocumentReferences(content, relPath);
                var relatedNodes = DocumentCodeReferenceExtractor.ExtractCodeFileReferences(content, projectContext, relPath).ToList();
                relatedNodes.AddRange(DocumentRouteReferenceExtractor.ExtractRouteReferences(content, projectContext));
                relatedNodes.AddRange(DocumentMcpToolReferenceExtractor.ExtractMcpToolReferences(content, mcpToolFiles));
                relatedNodes = relatedNodes
                    .Select(NormalizeRelatedNodeId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                logger.LogInformation("  {File} -> {Chunks} chunk(s)", relPath, chunks.Count);
                var requests = new List<KnowledgeDocumentIngestRequest>(chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    var id = DocumentChunkReferenceBuilder.BuildChunkDocumentId(projectContext, relPath, chunks.Count, i);
                    var relatedChunkIds = DocumentChunkReferenceBuilder.BuildAdjacentChunkIds(projectContext, relPath, chunks.Count, i);

                    requests.Add(new KnowledgeDocumentIngestRequest(
                        Content: chunks[i],
                        Id: id,
                        Source: relPath,
                        ProjectContext: projectContext,
                        RelatedNodeIdsCsv: relatedNodes.Count > 0 ? string.Join(",", relatedNodes) : null,
                        RelatedDocumentIdsCsv: DocumentChunkReferenceBuilder.BuildRelatedDocumentIdsCsv(relatedDocuments, relatedChunkIds)));
                }

                await client.IngestDocumentsAsync(requests, cancellationToken);
                count += requests.Count;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        return new DocumentStats(count);
    }

    private static string NormalizeRelatedNodeId(string nodeId)
    {
        const string apiEndpointMarker = "::ApiEndpoint::";
        var markerIndex = nodeId.IndexOf(apiEndpointMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return nodeId;

        var routeStart = nodeId.IndexOf(' ', markerIndex + apiEndpointMarker.Length);
        if (routeStart < 0 || routeStart == nodeId.Length - 1)
            return nodeId;

        var prefix = nodeId[..routeStart];
        var route = nodeId[(routeStart + 1)..];
        return $"{prefix} {DocumentRouteReferenceExtractor.NormalizeRouteTemplate(route)}";
    }
}
