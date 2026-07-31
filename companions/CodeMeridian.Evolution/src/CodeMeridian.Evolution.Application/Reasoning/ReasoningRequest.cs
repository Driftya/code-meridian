namespace CodeMeridian.Evolution.Application.Reasoning;

public sealed record ReasoningRequest(
    Guid InvocationId,
    string ProviderId,
    string Role,
    string Goal,
    IReadOnlyList<string> EvidenceIds,
    int MaximumOutputTokens,
    TimeSpan Timeout,
    string IdempotencyKey)
{
    public IReadOnlyList<ReasoningEvidence> Evidence { get; init; } = [];

    public string ProjectId { get; init; } = "meridian-evolution";
}
