namespace CodeMeridian.Evolution.Application.Ledger;

public sealed record CorrectionRequest(
    string Actor,
    string Summary,
    decimal Confidence,
    string IdempotencyKey);
