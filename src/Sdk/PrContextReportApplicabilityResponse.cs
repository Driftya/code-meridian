namespace CodeMeridian.Sdk;

public sealed record PrContextReportApplicabilityResponse(
    string Status,
    long? ProfileGeneration,
    string? TargetKind,
    string? Scope,
    IReadOnlyList<string> Reasons)
{
    public string ContractVersion { get; init; } = "1.0";
    public string? Project { get; init; }
    public string Coverage { get; init; } = "unknown";
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public IReadOnlyList<string> NextActions { get; init; } = [];
}
