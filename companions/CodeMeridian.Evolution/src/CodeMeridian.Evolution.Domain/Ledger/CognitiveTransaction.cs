namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed class CognitiveTransaction
{
    public CognitiveTransaction(
        Guid id,
        DateTimeOffset occurredAt,
        string actor,
        JournalEventKind kind,
        string summary,
        IReadOnlyList<EvidenceReference> evidence,
        IReadOnlyList<LedgerPosting> postings,
        string? causalParentId,
        string? authorityReference,
        string? correctsEntryId,
        string? idempotencyKey,
        decimal uncertainty)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(postings);

        Id = id;
        OccurredAt = occurredAt;
        Actor = actor;
        Kind = kind;
        Summary = summary;
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Postings = Array.AsReadOnly(postings.ToArray());
        CausalParentId = causalParentId;
        AuthorityReference = authorityReference;
        CorrectsEntryId = correctsEntryId;
        IdempotencyKey = idempotencyKey;
        Uncertainty = uncertainty;

        Validate();
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public string Actor { get; }

    public JournalEventKind Kind { get; }

    public string Summary { get; }

    public IReadOnlyList<EvidenceReference> Evidence { get; }

    public IReadOnlyList<LedgerPosting> Postings { get; }

    public string? CausalParentId { get; }

    public string? AuthorityReference { get; }

    public string? CorrectsEntryId { get; }

    public string? IdempotencyKey { get; }

    public decimal Uncertainty { get; }

    public static CognitiveTransaction Create(
        DateTimeOffset occurredAt,
        string actor,
        JournalEventKind kind,
        string summary,
        IEnumerable<EvidenceReference> evidence,
        IEnumerable<LedgerPosting> postings,
        string? causalParentId = null,
        string? authorityReference = null,
        string? correctsEntryId = null,
        string? idempotencyKey = null,
        decimal uncertainty = 0m)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(postings);

        return new CognitiveTransaction(
            Guid.NewGuid(),
            occurredAt,
            actor,
            kind,
            summary,
            evidence.ToArray(),
            postings.ToArray(),
            causalParentId,
            authorityReference,
            correctsEntryId,
            idempotencyKey,
            uncertainty);
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A cognitive transaction requires an id.", nameof(Id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);

        if (Evidence.Count == 0)
        {
            throw new InvalidOperationException("A cognitive transaction requires evidence.");
        }

        if (Postings.Count == 0)
        {
            throw new InvalidOperationException("A cognitive transaction requires at least one ledger posting.");
        }

        if (Uncertainty is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Uncertainty),
                Uncertainty,
                "Uncertainty must be between zero and one.");
        }

        foreach (var item in Evidence)
        {
            item.Validate();
        }

        foreach (var posting in Postings)
        {
            posting.Validate();
        }

        if (Kind == JournalEventKind.ActionRequested &&
            string.IsNullOrWhiteSpace(AuthorityReference))
        {
            throw new InvalidOperationException("Requested actions require an authority reference.");
        }

        if (Kind == JournalEventKind.Adjustment &&
            string.IsNullOrWhiteSpace(CorrectsEntryId))
        {
            throw new InvalidOperationException("Adjusting entries must reference the journal entry they correct.");
        }
    }
}
