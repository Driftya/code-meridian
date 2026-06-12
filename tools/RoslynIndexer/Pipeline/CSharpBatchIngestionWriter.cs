using CodeMeridian.Sdk;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpBatchIngestionWriter
{
    public static async Task BatchIngestNodesAsync(
        CodeMeridianClient client,
        ILogger logger,
        List<IngestNodeRequest> nodes,
        string projectContext,
        CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        var batches = nodes.Chunk(batchSize).ToArray();

        for (var i = 0; i < batches.Length; i++)
        {
            logger.LogInformation(
                "  Ingesting nodes batch {Current}/{Total}...", i + 1, batches.Length);

            foreach (var n in batches[i])
            {
                await client.IngestCodeNodeAsync(
                    n.Id, n.Name, n.Type,
                    namespacePath: n.Namespace,
                    filePath: n.FilePath,
                    lineNumber: n.LineNumber,
                    lineCount: n.LineCount,
                    summary: n.Summary,
                    sourceSnippet: n.SourceSnippet,
                    sourceHash: n.SourceHash,
                    projectContext: projectContext,
                    properties: n.Properties,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public static async Task BatchIngestEdgesAsync(
        CodeMeridianClient client,
        ILogger logger,
        List<IngestEdgeRequest> edges,
        CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var batches = edges.Chunk(batchSize).ToArray();

        for (var i = 0; i < batches.Length; i++)
        {
            logger.LogInformation(
                "  Ingesting edges batch {Current}/{Total}...", i + 1, batches.Length);

            var tasks = batches[i].Select(e => client.IngestRelationshipAsync(
                e.SourceId, e.TargetId, e.RelationshipType,
                isAsync: e.IsAsync,
                callSite: e.CallSite,
                paramCount: e.ParamCount,
                confidence: e.Confidence,
                properties: e.Properties,
                cancellationToken: cancellationToken));

            await Task.WhenAll(tasks);
        }
    }
}
