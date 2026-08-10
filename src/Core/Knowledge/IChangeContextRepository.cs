namespace CodeMeridian.Core.Knowledge;

public interface IChangeContextRepository
{
    Task UpsertAsync(ChangeContextEntry context, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChangeContextEntry>> ListForNodeAsync(
        string nodeId,
        int limit,
        CancellationToken cancellationToken = default);
}
