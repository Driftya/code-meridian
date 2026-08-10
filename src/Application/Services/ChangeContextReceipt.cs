namespace CodeMeridian.Application.Services;

public sealed record ChangeContextReceipt(
    string ContractVersion,
    string ContextId,
    string NodeId,
    string ContextKind,
    string Provenance,
    bool UserConfirmed,
    string Status,
    string? TargetSourceHashAtWrite);
