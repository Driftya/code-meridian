namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed record JournalEntry(
    long Sequence,
    DateTimeOffset AppendedAt,
    string PreviousHash,
    string Hash,
    CognitiveTransaction Transaction)
{
    public static JournalEntry Create(
        long sequence,
        DateTimeOffset appendedAt,
        string previousHash,
        CognitiveTransaction transaction)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be positive.");
        }

        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();

        previousHash ??= string.Empty;
        var hash = JournalHash.Compute(sequence, appendedAt, previousHash, transaction);
        return new JournalEntry(sequence, appendedAt, previousHash, hash, transaction);
    }

    public bool HasValidHash()
    {
        var expected = JournalHash.Compute(Sequence, AppendedAt, PreviousHash, Transaction);
        return string.Equals(expected, Hash, StringComparison.Ordinal);
    }
}
