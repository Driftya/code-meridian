namespace CodeMeridian.Application.Services;

public interface IKeywordGraphJobService
{
    Task<KeywordGraphJobSubmissionResult> StartRebuildAsync(
        string? projectContext = null,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default);

    Task<KeywordGraphJobSubmissionResult> StartClassifyAsync(
        string? projectContext = null,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default);

    Task<KeywordGraphJobStatus?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
}
