namespace CodeMeridian.Evolution.Application.Governance;

public sealed record GovernanceCommand(
    string Actor,
    string Reason,
    string IdempotencyKey);
