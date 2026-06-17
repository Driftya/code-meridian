namespace CodeMeridian.Core.KeywordGraph;

public interface IKeywordGraphRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeywordSourceNode>> GetKeywordSourceNodesAsync(
        KeywordSourceNodeQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeywordSourceNode>> GetKeywordSourceNodesByIdAsync(
        IReadOnlyCollection<string> sourceNodeIds,
        string? projectContext = null,
        CancellationToken cancellationToken = default);

    Task ReplaceKeywordsAsync(
        ReplaceKeywordRelationshipsCommand command,
        CancellationToken cancellationToken = default);

    Task RecalculateKeywordStatisticsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default);

    Task<int> GetKeywordSourceNodeCountAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeywordForClassification>> GetKeywordsForClassificationAsync(
        string? projectContext,
        int classificationVersion,
        CancellationToken cancellationToken = default);

    Task SaveKeywordClassificationsAsync(
        IReadOnlyCollection<KeywordClassificationResult> results,
        int classificationVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeywordRelatedNode>> FindRelatedByKeywordsAsync(
        KeywordRelatedNodeQuery query,
        CancellationToken cancellationToken = default);
}
