using CodeMeridian.Evolution.Domain.Ledger;
using CodeMeridian.Evolution.Infrastructure.Journal;

namespace CodeMeridian.Evolution.Infrastructure.Tests.Journal;

public sealed class InMemoryJournalStoreTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsyncCreatesContinuousHashChain()
    {
        var store = new InMemoryJournalStore();

        var first = await store.AppendAsync(
            CreateTransaction(1, "observation-1"),
            OccurredAt.AddMinutes(1));
        var second = await store.AppendAsync(
            CreateTransaction(2, "observation-2"),
            OccurredAt.AddMinutes(2));

        Assert.True(first.WasAppended);
        Assert.True(second.WasAppended);
        Assert.Equal(1, first.Entry.Sequence);
        Assert.Equal(2, second.Entry.Sequence);
        Assert.Equal(first.Entry.Hash, second.Entry.PreviousHash);
        Assert.True(second.Entry.HasValidHash());
    }

    [Fact]
    public async Task AppendAsyncRepeatedTransactionReturnsOriginalEntry()
    {
        var store = new InMemoryJournalStore();
        var transaction = CreateTransaction(1, "observation-1");

        var first = await store.AppendAsync(transaction, OccurredAt.AddMinutes(1));
        var repeated = await store.AppendAsync(transaction, OccurredAt.AddMinutes(2));
        var entries = await store.ReadAllAsync();

        Assert.True(first.WasAppended);
        Assert.False(repeated.WasAppended);
        Assert.Equal(first.Entry, repeated.Entry);
        Assert.Single(entries);
    }

    [Fact]
    public async Task AppendAsyncReusedIdempotencyKeyReturnsOriginalEntry()
    {
        var store = new InMemoryJournalStore();
        var first = CreateTransaction(1, "shared-key");
        var conflicting = CreateTransaction(2, "shared-key");

        var original = await store.AppendAsync(first, OccurredAt.AddMinutes(1));
        var repeated = await store.AppendAsync(conflicting, OccurredAt.AddMinutes(2));
        var entries = await store.ReadAllAsync();

        Assert.False(repeated.WasAppended);
        Assert.Equal(original.Entry, repeated.Entry);
        Assert.Single(entries);
    }

    [Fact]
    public async Task ReadAllAsyncReturnsAnImmutablePointInTimeSnapshot()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(
            CreateTransaction(1, "observation-1"),
            OccurredAt.AddMinutes(1));

        var snapshot = await store.ReadAllAsync();

        await store.AppendAsync(
            CreateTransaction(2, "observation-2"),
            OccurredAt.AddMinutes(2));

        Assert.Single(snapshot);
        Assert.False(snapshot is JournalEntry[]);
    }

    [Fact]
    public async Task ConcurrentAppendsProduceBalancedReplay()
    {
        var store = new InMemoryJournalStore();
        var appendTasks = Enumerable.Range(1, 32)
            .Select(index => store.AppendAsync(
                CreateTransaction(index, $"observation-{index}"),
                OccurredAt.AddMinutes(index)))
            .ToArray();

        await Task.WhenAll(appendTasks);

        var entries = await store.ReadAllAsync();
        var report = TrialBalanceService.Evaluate(entries);

        Assert.Equal(32, entries.Count);
        Assert.Equal(Enumerable.Range(1, 32).Select(index => (long)index), entries.Select(entry => entry.Sequence));
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public async Task OperationsHonorCancellation()
    {
        var store = new InMemoryJournalStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.AppendAsync(
                CreateTransaction(1, "observation-1"),
                OccurredAt,
                cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.ReadAllAsync(cancellation.Token));
    }

    private static CognitiveTransaction CreateTransaction(int index, string idempotencyKey)
    {
        var transactionId = Guid.Parse($"00000000-0000-0000-0000-{index:D12}");

        return new CognitiveTransaction(
            transactionId,
            OccurredAt.AddSeconds(index),
            "sensor:test",
            JournalEventKind.Observation,
            $"Record observation {index}.",
            [
                new EvidenceReference(
                    $"evidence-{index}",
                    "sensor:test",
                    $"Deterministic evidence {index}.",
                    OccurredAt.AddSeconds(index),
                    1m)
            ],
            [
                new LedgerPosting(
                    LedgerAccount.Research,
                    $"observation-{index}",
                    $"Observation {index}.",
                    $"evidence-{index}",
                    1m,
                    ReconciliationState.Pending)
            ],
            causalParentId: null,
            authorityReference: null,
            correctsEntryId: null,
            idempotencyKey,
            uncertainty: 0m);
    }
}
