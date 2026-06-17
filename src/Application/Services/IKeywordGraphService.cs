namespace CodeMeridian.Application.Services;

public interface IKeywordGraphService
{
    Task<string> RebuildKeywordGraphAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    Task<string> RefreshKeywordsAsync(
        IReadOnlyCollection<string> sourceNodeIds,
        string? projectContext = null,
        CancellationToken cancellationToken = default);

    Task<string> ClassifyKeywordsAsync(string? projectContext = null, CancellationToken cancellationToken = default);

    Task<string> FindRelatedKnowledgeAsync(
        string sourceNodeId,
        IReadOnlyList<string>? targetKinds = null,
        int? minimumSharedKeywords = null,
        double? minimumScore = null,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
