namespace CodeMeridian.Application.Services;

public sealed record ChangeContextView(
    string ContextId,
    string NodeId,
    string Statement,
    string ContextKind,
    string Provenance,
    bool UserConfirmed,
    string Status,
    DateTimeOffset CreatedAt,
    string? TargetSourceHashAtWrite);
