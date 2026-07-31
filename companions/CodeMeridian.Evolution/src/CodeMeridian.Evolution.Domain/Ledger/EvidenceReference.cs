namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed record EvidenceReference(
    string Id,
    string Source,
    string Description,
    DateTimeOffset ObservedAt,
    decimal Confidence)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);

        if (Confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Confidence),
                Confidence,
                "Evidence confidence must be between zero and one.");
        }
    }
}
