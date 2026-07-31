using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    public Task<JournalAppendResult> SetPausedAsync(
        bool isPaused,
        GovernanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);

        var occurredAt = timeProvider.GetUtcNow();
        var evidence = new EvidenceReference(
            $"authority:{command.Actor}:{command.IdempotencyKey}",
            "human-governance",
            command.Reason,
            occurredAt,
            1m);

        return AppendAsync(
            JournalEventKind.GovernanceChanged,
            command.Actor,
            isPaused ? "Pause requested." : "Resume requested.",
            [evidence],
            [
                new LedgerPosting(
                    LedgerAccount.System,
                    "governance:state",
                    isPaused ? "paused" : "running",
                    evidence.Id,
                    1m,
                    ReconciliationState.Reconciled),
                new LedgerPosting(
                    LedgerAccount.Authority,
                    "governance:last-change",
                    command.Reason,
                    evidence.Id,
                    1m,
                    ReconciliationState.Reconciled)
            ],
            command.IdempotencyKey,
            occurredAt,
            authorityReference: evidence.Id,
            cancellationToken: cancellationToken);
    }
}
