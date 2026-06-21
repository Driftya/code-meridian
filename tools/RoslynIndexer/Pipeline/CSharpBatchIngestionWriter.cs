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

            var requests = batches[i]
                .Select(n => new CodeNodeIngestRequest(
                    n.Id,
                    n.Name,
                    n.Type,
                    Namespace: n.Namespace,
                    FilePath: n.FilePath,
                    LineNumber: n.LineNumber,
                    LineCount: n.LineCount,
                    Summary: n.Summary,
                    SourceSnippet: n.SourceSnippet,
                    SourceHash: n.SourceHash,
                    FileRole: n.Properties is not null && n.Properties.TryGetValue("fileRole", out var fileRole) ? fileRole : null,
                    ProjectContext: projectContext,
                    Properties: n.Properties))
                .ToArray();

            await client.IngestCodeNodesAsync(requests, cancellationToken);
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

            var requests = batches[i]
                .Select(e => new CodeEdgeIngestRequest(
                    e.SourceId,
                    e.TargetId,
                    e.RelationshipType,
                    IsAsync: e.IsAsync,
                    CallSite: e.CallSite,
                    ParamCount: e.ParamCount,
                    Confidence: e.Confidence,
                    Properties: e.Properties))
                .ToArray();

            await client.IngestRelationshipsAsync(requests, cancellationToken);
        }
    }
}
