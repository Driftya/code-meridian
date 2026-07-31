namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed record LedgerPosting(
    LedgerAccount Account,
    string SubjectId,
    string Summary,
    string Provenance,
    decimal Confidence,
    ReconciliationState Reconciliation)
{
    public string ProjectId { get; init; } = "meridian-evolution";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProjectId);

        if (Confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Confidence),
                Confidence,
                "Posting confidence must be between zero and one.");
        }
    }
}
