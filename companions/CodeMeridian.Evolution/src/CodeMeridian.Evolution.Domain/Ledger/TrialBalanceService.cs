namespace CodeMeridian.Evolution.Domain.Ledger;

public static class TrialBalanceService
{
    public static TrialBalanceReport Evaluate(IReadOnlyList<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var violations = new List<TrialBalanceViolation>();
        var expectedSequence = 1L;
        var previousHash = string.Empty;

        foreach (var entry in entries.OrderBy(item => item.Sequence))
        {
            if (entry.Sequence != expectedSequence)
            {
                violations.Add(new TrialBalanceViolation(
                    entry.Sequence,
                    "sequence-gap",
                    $"Expected journal sequence {expectedSequence}, found {entry.Sequence}."));
                expectedSequence = entry.Sequence;
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                violations.Add(new TrialBalanceViolation(
                    entry.Sequence,
                    "broken-chain",
                    "The entry previous hash does not match the preceding journal hash."));
            }

            if (!entry.HasValidHash())
            {
                violations.Add(new TrialBalanceViolation(
                    entry.Sequence,
                    "invalid-hash",
                    "The journal entry hash does not match its canonical content."));
            }

            try
            {
                entry.Transaction.Validate();
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                violations.Add(new TrialBalanceViolation(
                    entry.Sequence,
                    "invalid-transaction",
                    exception.Message));
            }

            previousHash = entry.Hash;
            expectedSequence++;
        }

        return new TrialBalanceReport(
            entries.Count,
            entries.Count == 0 ? string.Empty : entries.MaxBy(item => item.Sequence)!.Hash,
            violations);
    }
}
