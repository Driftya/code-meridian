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
    IOptions<KeywordClassificationOptions> classificationOptions,
    ILogger<KeywordGraphService> logger) : IKeywordGraphService
{
    private readonly KeywordEnrichmentOptions keywordOptions = options.Value;
    private readonly KeywordClassificationOptions keywordClassificationOptions = classificationOptions.Value;

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

            var result = await RefreshSourceNodesAsync(batch, cancellationToken);
            processedCount += result.ProcessedCount;
            skippedCount += result.SkippedCount;
            updatedCount += result.UpdatedCount;
            keywordCount += result.KeywordCount;
            relationshipCount += result.RelationshipCount;

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

    public async Task<string> RefreshKeywordsAsync(
        IReadOnlyCollection<string> sourceNodeIds,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!keywordOptions.Enabled)
            return "Keyword enrichment is disabled. Set `KeywordEnrichment:Enabled` to `true` to refresh the derived keyword graph.";

        var normalizedIds = sourceNodeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedIds.Length == 0)
            return "No keyword source nodes were queued for refresh.";

        var sourceNodes = await keywordGraph.GetKeywordSourceNodesByIdAsync(
            normalizedIds,
            projectContext,
            cancellationToken);

        var result = await RefreshSourceNodesAsync(sourceNodes, cancellationToken);
        await keywordGraph.RecalculateKeywordStatisticsAsync(projectContext, cancellationToken);

        logger.LogInformation(
            "Incremental keyword refresh completed for project {ProjectId}. Queued {QueuedCount} nodes, found {ProcessedCount}, updated {UpdatedCount}.",
            projectContext ?? "all-projects",
            normalizedIds.Length,
            result.ProcessedCount,
            result.UpdatedCount);

        var sb = new StringBuilder();
        sb.AppendLine("## Keyword Graph Incremental Refresh");
        sb.AppendLine();
        sb.AppendLine($"Project: `{projectContext ?? "all-projects"}`");
        sb.AppendLine($"Queued **{normalizedIds.Length.ToString(CultureInfo.InvariantCulture)}** source nodes.");
        sb.AppendLine($"Found **{result.ProcessedCount.ToString(CultureInfo.InvariantCulture)}** source nodes.");
        sb.AppendLine($"Skipped **{result.SkippedCount.ToString(CultureInfo.InvariantCulture)}** unchanged nodes.");
        sb.AppendLine($"Updated **{result.UpdatedCount.ToString(CultureInfo.InvariantCulture)}** nodes.");
        sb.AppendLine($"Created **{result.KeywordCount.ToString(CultureInfo.InvariantCulture)}** derived keywords across **{result.RelationshipCount.ToString(CultureInfo.InvariantCulture)}** relationships.");
        return sb.ToString();
    }

    public async Task<string> ClassifyKeywordsAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        if (!keywordOptions.Enabled)
            return "Keyword enrichment is disabled. Set `KeywordEnrichment:Enabled` to `true` before classifying derived keywords.";

        if (!keywordClassificationOptions.Enabled)
            return "Keyword classification is disabled. Set `KeywordClassification:Enabled` to `true` to classify derived keywords.";

        var sourceNodeCount = await keywordGraph.GetKeywordSourceNodeCountAsync(projectContext, cancellationToken);
        if (sourceNodeCount <= 0)
            return $"No keyword source nodes found for `{projectContext ?? "all-projects"}`. Index code or rebuild the keyword graph first.";

        var keywords = await keywordGraph.GetKeywordsForClassificationAsync(
            projectContext,
            keywordClassificationOptions.ClassificationVersion,
            cancellationToken);

        if (keywords.Count == 0)
            return $"No keywords require classification for `{projectContext ?? "all-projects"}`. Current classification version: `{keywordClassificationOptions.ClassificationVersion}`.";

        var results = keywords
            .Select(keyword => ClassifyKeyword(keyword, sourceNodeCount))
            .ToArray();

        await keywordGraph.SaveKeywordClassificationsAsync(
            results,
            keywordClassificationOptions.ClassificationVersion,
            cancellationToken);

        var commonCount = results.Count(static result => result.IsCommon);
        var noiseCount = results.Count(static result => result.IsNoise);

        logger.LogInformation(
            "Keyword classification completed for project {ProjectId}. Processed {ProcessedCount} keywords, marked {CommonCount} common and {NoiseCount} noise.",
            projectContext ?? "all-projects",
            results.Length,
            commonCount,
            noiseCount);

        var sb = new StringBuilder();
        sb.AppendLine("## Keyword Classification");
        sb.AppendLine();
        sb.AppendLine($"Project: `{projectContext ?? "all-projects"}`");
        sb.AppendLine($"Classification version: `{keywordClassificationOptions.ClassificationVersion.ToString(CultureInfo.InvariantCulture)}`");
        sb.AppendLine();
        sb.AppendLine($"Processed **{results.Length.ToString(CultureInfo.InvariantCulture)}** keywords.");
        sb.AppendLine($"Marked **{commonCount.ToString(CultureInfo.InvariantCulture)}** keywords as common project terms.");
        sb.AppendLine($"Marked **{noiseCount.ToString(CultureInfo.InvariantCulture)}** keywords as noise.");
        sb.AppendLine();
        sb.AppendLine("Confidence: `heuristic`");
        sb.AppendLine("Classification uses configurable lexical rules and document-frequency thresholds to make shared-keyword matches more useful.");
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

    private KeywordClassificationResult ClassifyKeyword(KeywordForClassification keyword, int sourceNodeCount)
    {
        var normalizedValue = keyword.NormalizedValue.Trim();
        var documentFrequencyRatio = sourceNodeCount <= 0
            ? 0d
            : (double)keyword.DocumentFrequency / sourceNodeCount;
        var isCommon = documentFrequencyRatio >= keywordClassificationOptions.CommonDocumentFrequencyRatio;
        var classification = ResolveClassification(normalizedValue, isCommon);
        var isNoise = classification == KeywordClassification.Noise;
        var baseScore = keyword.DocumentFrequency <= 0
            ? 0d
            : (double)keyword.TotalFrequency / keyword.DocumentFrequency;
        var usefulnessScore = AdjustUsefulnessScore(baseScore, classification, isCommon, isNoise);

        return new KeywordClassificationResult
        {
            KeywordId = keyword.Id,
            Classification = classification,
            IsCommon = isCommon,
            IsNoise = isNoise,
            UsefulnessScore = usefulnessScore
        };
    }

    private KeywordClassification ResolveClassification(string value, bool isCommon)
    {
        if (Contains(keywordClassificationOptions.NoiseTerms, value))
            return KeywordClassification.Noise;

        if (isCommon)
            return KeywordClassification.CommonProjectTerm;

        if (Contains(keywordClassificationOptions.DomainTerms, value))
            return KeywordClassification.DomainConcept;

        if (Contains(keywordClassificationOptions.TechnicalTerms, value))
            return KeywordClassification.TechnicalConcept;

        if (Contains(keywordClassificationOptions.ToolingTerms, value))
            return KeywordClassification.ToolingConcept;

        if (Contains(keywordClassificationOptions.ArchitectureTerms, value))
            return KeywordClassification.ArchitectureConcept;

        if (Contains(keywordClassificationOptions.DiagnosticTerms, value))
            return KeywordClassification.DiagnosticConcept;

        return KeywordClassification.Unknown;
    }

    private static double AdjustUsefulnessScore(
        double score,
        KeywordClassification classification,
        bool isCommon,
        bool isNoise)
    {
        if (isNoise)
            return 0d;

        var adjustedScore = classification switch
        {
            KeywordClassification.DomainConcept => score * 1.4d,
            KeywordClassification.TechnicalConcept => score * 1.25d,
            KeywordClassification.ToolingConcept => score * 1.15d,
            KeywordClassification.ArchitectureConcept => score * 1.1d,
            KeywordClassification.DiagnosticConcept => score * 1.1d,
            KeywordClassification.CommonProjectTerm => score * 0.2d,
            _ => score
        };

        return isCommon
            ? adjustedScore * 0.35d
            : adjustedScore;
    }

    private static bool Contains(IEnumerable<string> values, string value) =>
        values.Contains(value, StringComparer.OrdinalIgnoreCase);

    private async Task<KeywordRefreshResult> RefreshSourceNodesAsync(
        IReadOnlyCollection<KeywordSourceNode> sourceNodes,
        CancellationToken cancellationToken)
    {
        var processedCount = 0;
        var skippedCount = 0;
        var updatedCount = 0;
        var keywordCount = 0;
        var relationshipCount = 0;

        foreach (var sourceNode in sourceNodes)
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

        return new KeywordRefreshResult(processedCount, skippedCount, updatedCount, keywordCount, relationshipCount);
    }

    private sealed record KeywordRefreshResult(
        int ProcessedCount,
        int SkippedCount,
        int UpdatedCount,
        int KeywordCount,
        int RelationshipCount);
}
