namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record CognitiveCycleRequest(
    string ProviderId,
    string Role,
    string? ProjectId,
    string? Goal,
    int MaximumAttentionItems,
    bool Force);

