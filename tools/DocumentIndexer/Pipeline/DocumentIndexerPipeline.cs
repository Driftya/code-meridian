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
                logger.LogInformation("  {File} -> {Chunks} chunk(s)", relPath, chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    var id = DocumentChunkReferenceBuilder.BuildChunkDocumentId(projectContext, relPath, chunks.Count, i);
                    var relatedChunkIds = DocumentChunkReferenceBuilder.BuildAdjacentChunkIds(projectContext, relPath, chunks.Count, i);

                    await client.IngestDocumentAsync(
                        content: chunks[i],
                        source: relPath,
                        projectContext: projectContext,
                        id: id,
                        relatedNodeIdsCsv: relatedNodes.Count > 0 ? string.Join(",", relatedNodes) : null,
                        relatedDocumentIdsCsv: DocumentChunkReferenceBuilder.BuildRelatedDocumentIdsCsv(relatedDocuments, relatedChunkIds),
                        cancellationToken: cancellationToken);

                    count++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        return new DocumentStats(count);
    }
}
