using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Domain.Tests;

public sealed class TrialBalanceServiceTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EvaluateValidChainIsBalanced()
    {
        var entries = CreateChain();

        var report = TrialBalanceService.Evaluate(entries);

        Assert.True(report.IsBalanced);
        Assert.Equal(2, report.EntryCount);
        Assert.Equal(entries[1].Hash, report.HeadHash);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void EvaluateOutOfOrderInputReplaysBySequence()
    {
        var entries = CreateChain();

        var report = TrialBalanceService.Evaluate([entries[1], entries[0]]);

        Assert.True(report.IsBalanced);
        Assert.Equal(entries[1].Hash, report.HeadHash);
    }

    [Fact]
    public void EvaluateSequenceGapReportsViolation()
    {
        var entries = CreateChain();
        var sequenceGap = JournalEntry.Create(
            3,
            entries[1].AppendedAt,
            entries[0].Hash,
            entries[1].Transaction);

        var report = TrialBalanceService.Evaluate([entries[0], sequenceGap]);

        var violation = Assert.Single(report.Violations);
        Assert.Equal("sequence-gap", violation.Code);
        Assert.Equal(3, violation.Sequence);
    }

    [Fact]
    public void EvaluateBrokenChainAndTamperedHashReportsBothViolations()
    {
        var entries = CreateChain();
        var tampered = entries[1] with
        {
            PreviousHash = new string('a', 64),
            Hash = new string('b', 64)
        };

        var report = TrialBalanceService.Evaluate([entries[0], tampered]);

        Assert.Contains(report.Violations, violation => violation.Code == "broken-chain");
        Assert.Contains(report.Violations, violation => violation.Code == "invalid-hash");
    }

    private static JournalEntry[] CreateChain()
    {
        var first = JournalEntry.Create(
            1,
            OccurredAt.AddMinutes(1),
            string.Empty,
            CreateTransaction(
                Guid.Parse("5e075328-ed9a-499f-9fa0-82b13d87700c"),
                "belief-1"));
        var second = JournalEntry.Create(
            2,
            OccurredAt.AddMinutes(2),
            first.Hash,
            CreateTransaction(
                Guid.Parse("17847bd9-23bf-4351-bfc6-69e4f267601e"),
                "belief-2"));

        return [first, second];
    }

    private static CognitiveTransaction CreateTransaction(Guid id, string subjectId)
    {
        return new CognitiveTransaction(
            id,
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
