namespace CodeMeridian.Evolution.Domain.Ledger;

public sealed record TrialBalanceViolation(
    long? Sequence,
    string Code,
    string Message);
