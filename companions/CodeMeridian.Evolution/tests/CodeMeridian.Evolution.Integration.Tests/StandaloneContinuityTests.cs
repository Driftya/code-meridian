using CodeMeridian.Evolution.Application.Goals;
using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Observations;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Infrastructure.Journal;
using CodeMeridian.Evolution.Infrastructure.Reasoning;

namespace CodeMeridian.Evolution.Integration.Tests;

public sealed class StandaloneContinuityTests
{
    [Fact]
    public async Task ReplayPreservesGoalsObservationsAndHashChain()
    {
        var store = new InMemoryJournalStore();
        var firstProcess = new CognitiveLedgerService(store, TimeProvider.System);
        await firstProcess.InitializeAsync(CancellationToken.None);
        var goalId = Guid.NewGuid();
        await firstProcess.CreateGoalAsync(
            new GoalRequest(
                goalId,
                "Preserve functional continuity",
                "integration-test",
                "A fresh service instance reconstructs the same ledger head.",
                null,
                0m,
                $"goal:{goalId:D}"),
            CancellationToken.None);
        var observation = await firstProcess.RecordObservationAsync(
            new ObservationRequest(
                "observation:continuity:1",
                "integration-sensor",
                "continuity-check",
                "The first process recorded state.",
                "information",
                DateTimeOffset.UtcNow,
                0.9m,
                "observation:continuity:1"),
            CancellationToken.None);

        var beforeRestart = await firstProcess.GetSnapshotAsync(
            CancellationToken.None);
        var reconstructedProcess = new CognitiveLedgerService(store, TimeProvider.System);
        var afterRestart = await reconstructedProcess.GetSnapshotAsync(
            CancellationToken.None);

        Assert.Equal(beforeRestart.EntryCount, afterRestart.EntryCount);
        Assert.Equal(beforeRestart.HeadHash, afterRestart.HeadHash);
        Assert.Contains(afterRestart.ActiveGoals, goal => goal.SubjectId == goalId.ToString("D"));
        Assert.Contains(
            afterRestart.Accounts.SelectMany(account => account.Items),
            item => item.SubjectId == "observation:continuity:1");

        await reconstructedProcess.ChallengeEntryAsync(
            observation.Entry.Sequence,
            new CorrectionRequest(
                "integration-test",
                "The observation requires human review.",
                1m,
                "challenge:continuity:1"),
            CancellationToken.None);

        var corrected = await reconstructedProcess.GetSnapshotAsync(
            CancellationToken.None);
        Assert.True(corrected.IsBalanced);
        Assert.Contains(
            corrected.Unresolved,
            item => item.Summary == "The observation requires human review.");
    }

    [Fact]
    public async Task PauseRejectsObservationAndReasoningWork()
    {
        var store = new InMemoryJournalStore();
        var ledger = new CognitiveLedgerService(store, TimeProvider.System);
        await ledger.InitializeAsync(CancellationToken.None);
        await ledger.SetPausedAsync(
            isPaused: true,
            new GovernanceCommand(
                "integration-test",
                "Exercise the global pause.",
                "pause:integration-test"),
            CancellationToken.None);
        var runtime = new ReasoningRuntime(
            [new FakeReasoningProvider()],
            ledger,
            TimeProvider.System);
        var observation = new ObservationRequest(
            "observation:paused",
            "integration-sensor",
            "pause-test",
            "This observation must not be admitted.",
            "information",
            DateTimeOffset.UtcNow,
            1m,
            "observation:paused");
        var reasoning = new ReasoningRequest(
            Guid.NewGuid(),
            "fake",
            "critic",
            "This invocation must not run.",
            ["evidence:1"],
            200,
            TimeSpan.FromSeconds(1),
            "reasoning:paused");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ledger.RecordObservationAsync(observation, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.InvokeAsync(reasoning, CancellationToken.None));
        Assert.Equal(2, (await ledger.GetJournalAsync(CancellationToken.None)).Count);
    }
}
