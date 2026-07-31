using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Projections;

public sealed record LedgerItemView(
    long Sequence,
    Guid TransactionId,
    JournalEventKind EventKind,
    DateTimeOffset OccurredAt,
    LedgerAccount Account,
    string SubjectId,
    string Summary,
    string Provenance,
    decimal Confidence,
    ReconciliationState Reconciliation)
{
    public string ProjectId { get; init; } = "meridian-evolution";
}
