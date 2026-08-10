namespace CodeMeridian.Application.Services;

public interface IHumanCognitiveSeedContextService
{
    Task<ChangeContextReceipt> RecordAsync(
        string nodeId,
        string statement,
        string contextKind,
        string provenance,
        bool userConfirmed,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<ChangeContextListResult> GetAsync(
        string nodeId,
        bool includeStale,
        int limit,
        CancellationToken cancellationToken = default);
}
