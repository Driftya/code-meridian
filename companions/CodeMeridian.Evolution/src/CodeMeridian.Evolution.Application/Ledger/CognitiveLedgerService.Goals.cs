using CodeMeridian.Evolution.Application.Goals;
using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Domain.Governance;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    public async Task<JournalAppendResult> CreateGoalAsync(
        GoalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SuccessCriteria);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        if (request.Id == Guid.Empty)
        {
            throw new ArgumentException("A goal requires an id.", nameof(request));
        }

        if (request.Budget < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A goal budget cannot be negative.");
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!GovernanceKernel.Allows(
                snapshot.AutonomyLevel,
                AutonomyLevel.Recommend,
                snapshot.IsPaused))
        {
            throw new InvalidOperationException(
                "Goal intake is blocked while the governance kernel is paused.");
        }

        var occurredAt = timeProvider.GetUtcNow();
        var goalId = request.Id.ToString("D");
        var evidence = new EvidenceReference(
            $"authority:{request.Actor}:{goalId}",
            "human-authorization",
            request.SuccessCriteria,
            occurredAt,
            1m);
        var deadline = request.Deadline?.ToString("O") ?? "none";

        return await AppendAsync(
            JournalEventKind.GoalAuthorized,
            request.Actor,
            request.Title,
            [evidence],
            [
                new LedgerPosting(
                    LedgerAccount.Goal,
                    goalId,
                    request.Title,
                    evidence.Id,
                    1m,
                    ReconciliationState.Pending),
                new LedgerPosting(
                    LedgerAccount.Commitment,
                    goalId,
                    $"Success: {request.SuccessCriteria}; deadline: {deadline}",
                    evidence.Id,
                    1m,
                    ReconciliationState.Pending),
                new LedgerPosting(
                    LedgerAccount.Authority,
                    goalId,
                    request.Actor,
                    evidence.Id,
                    1m,
                    ReconciliationState.Reconciled),
                new LedgerPosting(
                    LedgerAccount.Resource,
                    goalId,
                    $"Budget: {request.Budget}",
                    evidence.Id,
                    1m,
                    ReconciliationState.Pending)
            ],
            request.IdempotencyKey,
            occurredAt,
            authorityReference: evidence.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalAppendResult> PauseGoalAsync(
        Guid goalId,
        GovernanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);

        var subjectId = goalId.ToString("D");
        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var sourceEntry = entries
            .Where(entry => entry.Transaction.Postings.Any(posting =>
                posting.Account == LedgerAccount.Goal &&
                string.Equals(posting.SubjectId, subjectId, StringComparison.Ordinal)))
            .MaxBy(entry => entry.Sequence)
            ?? throw new KeyNotFoundException($"Goal '{subjectId}' was not found.");
        var occurredAt = timeProvider.GetUtcNow();
        var evidence = new EvidenceReference(
            $"journal:{sourceEntry.Sequence}",
            "cognitive-ledger",
            command.Reason,
            occurredAt,
            1m);

        return await AppendAsync(
            JournalEventKind.GoalPaused,
            command.Actor,
            command.Reason,
            [evidence],
            [
                new LedgerPosting(
                    LedgerAccount.Goal,
                    subjectId,
                    sourceEntry.Transaction.Summary,
                    evidence.Id,
                    1m,
                    ReconciliationState.Disputed),
                new LedgerPosting(
                    LedgerAccount.Commitment,
                    subjectId,
                    command.Reason,
                    evidence.Id,
                    1m,
                    ReconciliationState.Disputed)
            ],
            command.IdempotencyKey,
            occurredAt,
            causalParentId: sourceEntry.Transaction.Id.ToString("D"),
            authorityReference: $"human:{command.Actor}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
