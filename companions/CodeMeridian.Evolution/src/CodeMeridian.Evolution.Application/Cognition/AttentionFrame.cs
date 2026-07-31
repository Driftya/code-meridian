namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record AttentionFrame(
    Guid Id,
    DateTimeOffset CompiledAt,
    string ProjectId,
    IReadOnlyList<AttentionSelection> Selections);

