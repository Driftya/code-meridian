using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Cognition;

public sealed class CognitiveMind(
    CognitiveLedgerService ledgerService,
    ReasoningRuntime reasoningRuntime,
    TimeProvider timeProvider)
{
    public async Task<CognitiveCycleResult> RunCycleAsync(
        CognitiveCycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Role);
        var snapshot = await ledgerService
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            throw new InvalidOperationException(
                "Autonomous cognition is blocked while the governance kernel is paused.");
        }

        var projectId = string.IsNullOrWhiteSpace(request.ProjectId)
            ? "meridian-evolution"
            : request.ProjectId;
        var lastCycleSequence = request.Force
            ? 0L
            : snapshot.Accounts
                .Single(account => account.Account == LedgerAccount.System)
                .Items
                .FirstOrDefault(item =>
                    string.Equals(
                        item.SubjectId,
                        $"cognition:last-cycle:{projectId}",
                        StringComparison.Ordinal))
                ?.Sequence ?? 0L;
        var attention = AttentionCompiler.Compile(
            snapshot,
            lastCycleSequence,
            projectId,
            request.MaximumAttentionItems);

        if (attention.Selections.Count == 0)
        {
            return new CognitiveCycleResult(
                Guid.NewGuid(),
                CognitiveCycleStatus.Idle,
                projectId,
                attention,
                null,
                null,
                snapshot.Affect,
                snapshot.Drives,
                null);
        }

        var cycleId = Guid.NewGuid();
        var goal = string.IsNullOrWhiteSpace(request.Goal)
            ? $"Investigate the selected {projectId} evidence, reduce uncertainty, and propose only bounded reversible next steps."
            : request.Goal;
        var evidence = attention.Selections
            .Select(selection => new ReasoningEvidence(
                selection.Item.SubjectId,
                selection.Item.Summary,
                selection.Item.Provenance,
                selection.Item.Confidence,
                selection.Item.ProjectId))
            .ToArray();
        var reasoningRequest = new ReasoningRequest(
            cycleId,
            request.ProviderId,
            request.Role,
            goal,
            evidence.Select(item => item.Id).ToArray(),
            1200,
            TimeSpan.FromSeconds(45),
            $"cognitive-cycle:reasoning:{cycleId:D}")
        {
            Evidence = evidence,
            ProjectId = projectId
        };
        var reasoning = await reasoningRuntime
            .InvokeAsync(reasoningRequest, cancellationToken)
            .ConfigureAwait(false);
        var simulation = MentalSimulationEngine.Simulate(attention, reasoning);
        var occurredAt = timeProvider.GetUtcNow();
        var stimulus = new AffectStimulus(
            Reward: reasoning.Abstained ? -0.1m : 0.15m,
            Novelty: Math.Clamp(attention.Selections.Count / 8m, 0m, 1m),
            PredictionError: reasoning.Uncertainty,
            Effort: 0.2m,
            Threat: attention.Selections.Any(selection =>
                selection.Item.Account == LedgerAccount.Attention) ? 0.25m : 0m);
        var affect = CognitiveHomeostasis.Apply(snapshot.Affect, stimulus, occurredAt);
        var drives = CognitiveHomeostasis.DeriveDrives(affect);
        var journal = await ledgerService.RecordCognitiveCycleAsync(
            cycleId,
            attention,
            reasoning,
            simulation,
            affect,
            drives,
            cancellationToken).ConfigureAwait(false);
        var status = reasoning.Abstained
            ? CognitiveCycleStatus.Abstained
            : projectId is "codemeridian" or "meridian-evolution"
                ? CognitiveCycleStatus.CandidateProposed
                : CognitiveCycleStatus.Reflected;

        return new CognitiveCycleResult(
            cycleId,
            status,
            projectId,
            attention,
            reasoning,
            simulation,
            affect,
            drives,
            journal);
    }

    public async Task<AffectState> ApplyStimulusAsync(
        AffectStimulusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stimulus);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        var snapshot = await ledgerService
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            throw new InvalidOperationException(
                "Affect updates are blocked while the governance kernel is paused.");
        }

        var affect = CognitiveHomeostasis.Apply(
            snapshot.Affect,
            request.Stimulus,
            timeProvider.GetUtcNow());
        await ledgerService.RecordAffectStimulusAsync(
            request,
            affect,
            CognitiveHomeostasis.DeriveDrives(affect),
            cancellationToken).ConfigureAwait(false);
        return affect;
    }
}
