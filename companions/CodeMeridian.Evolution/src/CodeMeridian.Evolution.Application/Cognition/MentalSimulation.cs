namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record MentalSimulation(
    Guid Id,
    string ProjectId,
    string Focus,
    string ExpectedOutcome,
    IReadOnlyList<string> Alternatives,
    IReadOnlyList<string> Risks,
    bool RequiresHumanApproval);

