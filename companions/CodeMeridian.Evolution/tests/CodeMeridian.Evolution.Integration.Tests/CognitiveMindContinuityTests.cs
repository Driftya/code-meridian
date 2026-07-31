using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Observations;
using CodeMeridian.Evolution.Application.Projects;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Domain.Ledger;
using CodeMeridian.Evolution.Infrastructure.Journal;
using CodeMeridian.Evolution.Infrastructure.Reasoning;

namespace CodeMeridian.Evolution.Integration.Tests;

public sealed class CognitiveMindContinuityTests
{
    [Fact]
    public async Task CyclePersistsAffectSimulationAndApprovalWithoutMergingEntities()
    {
        var store = new InMemoryJournalStore();
        var ledger = new CognitiveLedgerService(store, TimeProvider.System);
        await ledger.InitializeAsync(CancellationToken.None);
        await ledger.RecordObservationAsync(
            new ObservationRequest(
                "codemeridian:diagnostic:1",
                "codemeridian-graph",
                "code-diagnostic",
                "A graph diagnostic indicates an untested edge.",
                "warning",
                DateTimeOffset.UtcNow,
                0.9m,
                "codemeridian:diagnostic:1")
            {
                ProjectId = "codemeridian",
                TrustLevel = "authenticated-code-graph"
            },
            CancellationToken.None);
        var runtime = new ReasoningRuntime(
            [new FakeReasoningProvider()],
            ledger,
            TimeProvider.System);
        var mind = new CognitiveMind(ledger, runtime, TimeProvider.System);

        var result = await mind.RunCycleAsync(
            new CognitiveCycleRequest(
                "fake",
                "researcher",
                "codemeridian",
                null,
                8,
                Force: false),
            CancellationToken.None);

        Assert.Equal(CognitiveCycleStatus.CandidateProposed, result.Status);
        Assert.NotNull(result.Simulation);
        Assert.True(result.Simulation.RequiresHumanApproval);
        var candidateId = $"candidate:{result.Simulation.Id:D}";
        var beforeApproval = await ledger.GetSnapshotAsync(CancellationToken.None);
        var candidate = beforeApproval.Accounts
            .Single(account => account.Account == LedgerAccount.Action)
            .Items
            .Single(item => item.SubjectId == candidateId);
        Assert.Equal("codemeridian", candidate.ProjectId);
        Assert.Equal(ReconciliationState.Pending, candidate.Reconciliation);

        await ledger.ApproveCandidateAsync(
            candidateId,
            new CandidateApprovalRequest(
                "human:reviewer",
                "Approve isolated preparation only.",
                $"approve:{result.CycleId:D}"),
            CancellationToken.None);

        var reconstructed = new CognitiveLedgerService(store, TimeProvider.System);
        var afterApproval = await reconstructed.GetSnapshotAsync(CancellationToken.None);
        var approved = afterApproval.Accounts
            .Single(account => account.Account == LedgerAccount.Action)
            .Items
            .Single(item => item.SubjectId == candidateId);
        Assert.Equal(ReconciliationState.Reconciled, approved.Reconciliation);
        Assert.Equal("codemeridian", approved.ProjectId);
        Assert.NotEmpty(afterApproval.Drives);
        Assert.NotEmpty(afterApproval.HeadHash);
    }
}
