using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Infrastructure.Journal;

public sealed class InMemoryJournalStore : IJournalStore
{
    private readonly Dictionary<string, JournalEntry> entriesByIdempotencyKey =
        new(StringComparer.Ordinal);
    private readonly List<JournalEntry> entries = [];
    private readonly object sync = new();

    public Task<JournalAppendResult> AppendAsync(
        CognitiveTransaction transaction,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var idempotencyKey = NormalizeIdempotencyKey(transaction.IdempotencyKey);

            if (idempotencyKey is not null &&
                entriesByIdempotencyKey.TryGetValue(idempotencyKey, out var existingEntry))
            {
                return Task.FromResult(new JournalAppendResult(existingEntry, WasAppended: false));
            }

            var previousHash = entries.Count == 0 ? string.Empty : entries[^1].Hash;
            var entry = JournalEntry.Create(
                entries.Count + 1L,
                appendedAt,
                previousHash,
                transaction);

            entries.Add(entry);

            if (idempotencyKey is not null)
            {
                entriesByIdempotencyKey.Add(idempotencyKey, entry);
            }

            return Task.FromResult(new JournalAppendResult(entry, WasAppended: true));
        }
    }

    public Task<IReadOnlyList<JournalEntry>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<JournalEntry> snapshot = Array.AsReadOnly(entries.ToArray());
            return Task.FromResult(snapshot);
        }
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        return string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey;
    }

}
