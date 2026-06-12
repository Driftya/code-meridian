using System.Globalization;
using System.Text;
using CodeMeridian.Core.KeywordGraph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Services;

public sealed class KeywordGraphService(
    IKeywordGraphRepository keywordGraph,
    IKeywordExtractionService extractionService,
    IOptions<KeywordEnrichmentOptions> options,
    ILogger<KeywordGraphService> logger) : IKeywordGraphService
{
    private readonly KeywordEnrichmentOptions keywordOptions = options.Value;

    public async Task<string> RebuildKeywordGraphAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        if (!keywordOptions.Enabled)
            return "Keyword enrichment is disabled. Set `KeywordEnrichment:Enabled` to `true` to rebuild the derived keyword graph.";

        var processedCount = 0;
        var skippedCount = 0;
        var updatedCount = 0;
        var relationshipCount = 0;
        var keywordCount = 0;
        var skip = 0;

        while (true)
        {
            var batch = await keywordGraph.GetKeywordSourceNodesAsync(
                new KeywordSourceNodeQuery
                {
                    ProjectContext = projectContext,
                    Skip = skip,
                    Take = keywordOptions.BatchSize
                },
                cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var sourceNode in batch)
            {
                processedCount++;
                var extraction = extractionService.Extract(sourceNode);

                if (string.Equals(sourceNode.ExistingChecksum, extraction.Checksum, StringComparison.Ordinal))
                {
                    skippedCount++;
                    continue;
                }

                await keywordGraph.ReplaceKeywordsAsync(
                    new ReplaceKeywordRelationshipsCommand
                    {
                        SourceNodeId = sourceNode.Id,
                        KeywordTextChecksum = extraction.Checksum,
                        EnrichmentVersion = keywordOptions.EnrichmentVersion,
                        Keywords = extraction.Keywords
                    },
                    cancellationToken);

                updatedCount++;
                keywordCount += extraction.Keywords.Count;
                relationshipCount += extraction.Keywords.Count;
                logger.LogTrace("Extracted {KeywordCount} keywords for node {NodeId}.", extraction.Keywords.Count, sourceNode.Id);
            }

            if (batch.Count < keywordOptions.BatchSize)
                break;

            skip += batch.Count;
        }

        await keywordGraph.RecalculateKeywordStatisticsAsync(projectContext, cancellationToken);

        logger.LogInformation(
            "Keyword enrichment completed for project {ProjectId}. Processed {ProcessedCount} nodes, skipped {SkippedCount}, created {KeywordCount} keywords, updated {RelationshipCount} relationships.",
            projectContext ?? "all-projects",
            processedCount,
            skippedCount,
            keywordCount,
            relationshipCount);

        var sb = new StringBuilder();
        sb.AppendLine("## Keyword Graph Rebuild");
        sb.AppendLine();
        sb.AppendLine($"Project: `{projectContext ?? "all-projects"}`");
        sb.AppendLine();
        sb.AppendLine($"Processed **{processedCount.ToString(CultureInfo.InvariantCulture)}** source nodes.");
        sb.AppendLine($"Skipped **{skippedCount.ToString(CultureInfo.InvariantCulture)}** unchanged nodes.");
        sb.AppendLine($"Updated **{updatedCount.ToString(CultureInfo.InvariantCulture)}** nodes.");
        sb.AppendLine($"Created **{keywordCount.ToString(CultureInfo.InvariantCulture)}** derived keywords across **{relationshipCount.ToString(CultureInfo.InvariantCulture)}** relationships.");
        sb.AppendLine();
        sb.AppendLine("Confidence: `lexical`");
        sb.AppendLine("Keywords are derived from existing graph and knowledge-document text; they do not replace structural graph edges or embeddings.");
        return sb.ToString();
    }

    public async Task<string> FindRelatedKnowledgeAsync(
        string sourceNodeId,
        IReadOnlyList<string>? targetKinds = null,
        int? minimumSharedKeywords = null,
        double? minimumScore = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeId))
            return "Missing `sourceNodeId`. Pass the canonical graph node ID you want to expand from.";

        var results = await keywordGraph.FindRelatedByKeywordsAsync(
            new KeywordRelatedNodeQuery
            {
                SourceNodeId = sourceNodeId,
                TargetKinds = NormalizeKinds(targetKinds),
                MinimumSharedKeywords = minimumSharedKeywords ?? keywordOptions.MinimumSharedKeywords,
                MinimumScore = minimumScore ?? keywordOptions.MinimumScore,
                MaximumDocumentFrequencyRatio = keywordOptions.MaximumDocumentFrequencyRatio,
                Limit = limit
            },
            cancellationToken);

        if (results.Count == 0)
            return $"No related knowledge found for `{sourceNodeId}`. Rebuild the keyword graph first or loosen `minimumSharedKeywords` / `minimumScore`.";

        var sb = new StringBuilder();
        sb.AppendLine("## Related Knowledge");
        sb.AppendLine();
        sb.AppendLine($"Source: `{sourceNodeId}`");
        sb.AppendLine($"Confidence: `lexical`");
        sb.AppendLine();
        sb.AppendLine($"Found **{results.Count.ToString(CultureInfo.InvariantCulture)}** related nodes:");
        sb.AppendLine();
        sb.AppendLine("| Rank | Kind | Target | Shared keywords | Score |");
        sb.AppendLine("|---:|---|---|---:|---:|");

        for (var index = 0; index < results.Count; index++)
        {
            var item = results[index];
            sb.AppendLine(
                $"| {index + 1} | `{item.TargetKind}` | `{item.TargetNodeId}` | {item.SharedKeywordCount.ToString(CultureInfo.InvariantCulture)} | {item.Score.ToString("0.###", CultureInfo.InvariantCulture)} |");
        }

        sb.AppendLine();

        for (var index = 0; index < results.Count; index++)
        {
            var item = results[index];
            sb.AppendLine($"### [{index + 1}] `{item.TargetNodeId}`");
            sb.AppendLine($"Kind: `{item.TargetKind}`");
            sb.AppendLine($"Shared keywords: {item.SharedKeywordCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Score: {item.Score.ToString("0.###", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Matched keywords: {string.Join(", ", item.MatchedKeywords.Select(static keyword => $"`{keyword}`"))}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> NormalizeKinds(IReadOnlyList<string>? targetKinds)
    {
        if (targetKinds is null || targetKinds.Count == 0)
            return [];

        return targetKinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Select(kind => kind.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
