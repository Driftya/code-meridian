namespace CodeMeridian.Evolution.Application.Reasoning;

public sealed record ReasoningResult(
    Guid InvocationId,
    string ProviderId,
    string Summary,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Alternatives,
    decimal Uncertainty,
    bool Abstained,
    string? ContinuationToken);
