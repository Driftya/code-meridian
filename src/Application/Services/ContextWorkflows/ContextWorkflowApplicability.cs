namespace CodeMeridian.Application.Services.ContextWorkflows;

public sealed record ContextWorkflowApplicability(
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

    public string ToMarkdown(string tool) => $"Tool applicability status: {Status}. Tool: {tool}. "
        + $"Project: {Project ?? "unknown"}; generation: {ProfileGeneration?.ToString() ?? "unknown"}; scope: {Scope ?? "unknown"}; coverage: {Coverage}. "
        + string.Join(" ", Reasons.Concat(Limitations).Concat(NextActions));
}
