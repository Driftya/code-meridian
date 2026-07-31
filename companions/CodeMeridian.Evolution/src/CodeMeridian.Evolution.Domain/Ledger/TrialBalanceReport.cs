namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed record TrialBalanceReport(
    long EntryCount,
    string HeadHash,
    IReadOnlyList<TrialBalanceViolation> Violations)
{
    public bool IsBalanced => Violations.Count == 0;
}
