using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    public Task<JournalAppendResult> RecordCognitiveCycleAsync(
        Guid cycleId,
        AttentionFrame attention,
        ReasoningResult reasoning,
        MentalSimulation simulation,
        AffectState affect,
        IReadOnlyList<DriveState> drives,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attention);
        ArgumentNullException.ThrowIfNull(reasoning);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(affect);
        ArgumentNullException.ThrowIfNull(drives);

        if (cycleId == Guid.Empty)
        {
            throw new ArgumentException("A cognitive cycle requires an id.", nameof(cycleId));
        }

        var occurredAt = timeProvider.GetUtcNow();
        var evidence = attention.Selections
            .Select(selection => new EvidenceReference(
                selection.Item.SubjectId,
                selection.Item.Provenance,
                selection.Item.Summary,
                selection.Item.OccurredAt,
                selection.Item.Confidence))
            .ToArray();
        var provenance = $"cognitive-cycle:{cycleId:D}";
        var postings = CognitiveStatePostingFactory
            .Create(affect, drives, provenance)
            .ToList();

        postings.AddRange(attention.Selections.Select(selection =>
            new LedgerPosting(
                LedgerAccount.Attention,
                $"cycle:{cycleId:D}:{selection.Item.SubjectId}",
                selection.Reason,
                selection.Item.Provenance,
                selection.Item.Confidence,
                ReconciliationState.Reconciled)
            {
                ProjectId = attention.ProjectId
            }));
        postings.Add(new LedgerPosting(
            LedgerAccount.Belief,
            $"cycle:{cycleId:D}:interpretation",
            reasoning.Summary,
            provenance,
            1m - reasoning.Uncertainty,
            ReconciliationState.Pending)
        {
            ProjectId = attention.ProjectId
        });
        postings.Add(new LedgerPosting(
            LedgerAccount.Simulation,
            simulation.Id.ToString("D"),
            simulation.ExpectedOutcome,
            provenance,
            1m - reasoning.Uncertainty,
            ReconciliationState.Pending)
        {
            ProjectId = attention.ProjectId
        });
        postings.Add(new LedgerPosting(
            LedgerAccount.System,
            $"cognition:last-cycle:{attention.ProjectId}",
            cycleId.ToString("D"),
            provenance,
            1m,
            ReconciliationState.Reconciled)
        {
            ProjectId = attention.ProjectId
        });

        var candidateProposed = !reasoning.Abstained &&
                                attention.ProjectId is "codemeridian" or "meridian-evolution";

        if (candidateProposed)
        {
            postings.Add(new LedgerPosting(
                LedgerAccount.Action,
                $"candidate:{simulation.Id:D}",
                reasoning.Summary,
                provenance,
                1m - reasoning.Uncertainty,
                ReconciliationState.Pending)
            {
                ProjectId = attention.ProjectId
            });
        }

        return AppendAsync(
            candidateProposed
                ? JournalEventKind.CandidateProposed
                : JournalEventKind.CognitiveCycleCompleted,
            "mind:executive-loop",
            reasoning.Abstained
                ? "The cognitive cycle abstained."
                : candidateProposed
                    ? $"Simulated a bounded next step for {attention.ProjectId}."
                    : $"Recorded a bounded reflection for {attention.ProjectId}.",
            evidence,
            postings,
            $"cognitive-cycle:{cycleId:D}",
            occurredAt,
            causalParentId: attention.Selections[0].Item.TransactionId.ToString("D"),
            uncertainty: reasoning.Uncertainty,
            cancellationToken: cancellationToken);
    }

    public Task<JournalAppendResult> RecordAffectStimulusAsync(
        AffectStimulusRequest request,
        AffectState affect,
        IReadOnlyList<DriveState> drives,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(affect);
        ArgumentNullException.ThrowIfNull(drives);
        var occurredAt = affect.UpdatedAt;
        var evidence = new EvidenceReference(
            $"stimulus:{request.IdempotencyKey}",
            request.Source,
            request.Reason,
            occurredAt,
            1m);
        var postings = CognitiveStatePostingFactory
            .Create(affect, drives, evidence.Id)
            .Select(posting => posting with { ProjectId = request.ProjectId })
            .ToArray();

        return AppendAsync(
            JournalEventKind.AffectUpdated,
            request.Actor,
            request.Reason,
            [evidence],
            postings,
            request.IdempotencyKey,
            occurredAt,
            cancellationToken: cancellationToken);
    }
}
