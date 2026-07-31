using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Journal;

public interface IJournalStore
{
    Task<JournalAppendResult> AppendAsync(
        CognitiveTransaction transaction,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JournalEntry>> ReadAllAsync(
        CancellationToken cancellationToken = default);
}
