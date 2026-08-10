namespace CodeMeridian.Application.Services;

public sealed record ChangeContextListResult(
    string ContractVersion,
    string NodeId,
    bool TargetFound,
    IReadOnlyList<ChangeContextView> Items,
    bool Truncated,
    string TrustNotice);
