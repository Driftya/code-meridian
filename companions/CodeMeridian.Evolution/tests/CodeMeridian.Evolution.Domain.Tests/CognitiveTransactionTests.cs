using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Domain.Tests;

public sealed class CognitiveTransactionTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorSnapshotsEvidenceAndPostings()
    {
        var evidence = new[] { CreateEvidence("evidence-1") };
        var postings = new[] { CreatePosting("belief-1") };

        var transaction = new CognitiveTransaction(
            Guid.Parse("0db7b3f6-1074-4ed4-a524-1bd8f535fbd8"),
            OccurredAt,
            "human:researcher",
            JournalEventKind.BeliefRecorded,
            "Record an evidence-backed belief.",
            evidence,
            postings,
            causalParentId: null,
            authorityReference: null,
            correctsEntryId: null,
            idempotencyKey: "belief-1",
            uncertainty: 0.2m);

        evidence[0] = CreateEvidence("mutated-evidence");
        postings[0] = CreatePosting("mutated-belief");

        Assert.Equal("evidence-1", transaction.Evidence[0].Id);
        Assert.Equal("belief-1", transaction.Postings[0].SubjectId);
        Assert.False(transaction.Evidence is EvidenceReference[]);
        Assert.False(transaction.Postings is LedgerPosting[]);
    }

    [Fact]
    public void CreateActionRequestWithoutAuthorityThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CognitiveTransaction.Create(
                OccurredAt,
                "agent:executive",
                JournalEventKind.ActionRequested,
                "Request a bounded discriminating action.",
                [CreateEvidence("evidence-1")],
                [CreatePosting("action-1")]));

        Assert.Contains("authority reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateAdjustmentWithoutCorrectedEntryThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CognitiveTransaction.Create(
                OccurredAt,
                "human:reviewer",
                JournalEventKind.Adjustment,
                "Correct a challenged memory.",
                [CreateEvidence("challenge-1")],
                [CreatePosting("memory-1")]));

        Assert.Contains("reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void CreateUncertaintyOutsideUnitIntervalThrows(string value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CognitiveTransaction.Create(
                OccurredAt,
                "agent:reasoner",
                JournalEventKind.InterpretationRecorded,
                "Interpret an observation.",
                [CreateEvidence("evidence-1")],
                [CreatePosting("belief-1")],
                uncertainty: decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static EvidenceReference CreateEvidence(string id)
    {
        return new EvidenceReference(
            id,
            "sensor:test",
            "Deterministic test evidence.",
            OccurredAt,
            0.9m);
    }

    private static LedgerPosting CreatePosting(string subjectId)
    {
        return new LedgerPosting(
            LedgerAccount.Belief,
            subjectId,
            "A test belief.",
            "evidence-1",
            0.8m,
            ReconciliationState.Pending);
    }
}
