using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Domain.Tests;

public sealed class JournalEntryTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateComputesDeterministicHash()
    {
        var transaction = CreateTransaction("belief-1");

        var first = JournalEntry.Create(1, OccurredAt.AddMinutes(1), string.Empty, transaction);
        var second = JournalEntry.Create(1, OccurredAt.AddMinutes(1), string.Empty, transaction);

        Assert.Equal(first.Hash, second.Hash);
        Assert.True(first.HasValidHash());
    }

    [Fact]
    public void HasValidHashDetectsTampering()
    {
        var entry = JournalEntry.Create(
            1,
            OccurredAt.AddMinutes(1),
            string.Empty,
            CreateTransaction("belief-1"));

        var tampered = entry with { Hash = new string('0', 64) };

        Assert.False(tampered.HasValidHash());
    }

    [Fact]
    public void CreateUsesPreviousEntryHashToFormChain()
    {
        var first = JournalEntry.Create(
            1,
            OccurredAt.AddMinutes(1),
            string.Empty,
            CreateTransaction("belief-1"));
        var second = JournalEntry.Create(
            2,
            OccurredAt.AddMinutes(2),
            first.Hash,
            CreateTransaction("belief-2"));

        Assert.Equal(first.Hash, second.PreviousHash);
        Assert.True(second.HasValidHash());
    }

    [Fact]
    public void CreateNonPositiveSequenceThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JournalEntry.Create(0, OccurredAt, string.Empty, CreateTransaction("belief-1")));
    }

    private static CognitiveTransaction CreateTransaction(string subjectId)
    {
        return new CognitiveTransaction(
            Guid.Parse(subjectId == "belief-1"
                ? "82e7de02-8d24-4c05-a34e-159ff36cdbb3"
                : "278cf85d-0ae8-47c3-867a-d7b44630be26"),
            OccurredAt,
            "agent:reasoner",
            JournalEventKind.BeliefRecorded,
            $"Record {subjectId}.",
            [
                new EvidenceReference(
                    $"evidence-{subjectId}",
                    "sensor:test",
                    "Deterministic test evidence.",
                    OccurredAt,
                    0.9m)
            ],
            [
                new LedgerPosting(
                    LedgerAccount.Belief,
                    subjectId,
                    "A test belief.",
                    $"evidence-{subjectId}",
                    0.8m,
                    ReconciliationState.Pending)
            ],
            causalParentId: null,
            authorityReference: null,
            correctsEntryId: null,
            idempotencyKey: subjectId,
            uncertainty: 0.2m);
    }
}
