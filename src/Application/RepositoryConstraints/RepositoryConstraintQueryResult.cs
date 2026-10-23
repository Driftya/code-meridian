using System.Text;
using CodeMeridian.Core.RepositoryConstraints;

namespace CodeMeridian.Application.RepositoryConstraints;

public sealed record RepositoryConstraintQueryResult(
    string Project,
    string? TargetPath,
    long? Generation,
    long? LatestGeneration,
    string Status,
    string Coverage,
    string? PolicyPath,
    string? PolicyFingerprint,
    RepositoryConstraintResult? Evaluation,
    IReadOnlyList<RepositoryConstraintDiagnostic> Diagnostics)
{
    public string ContractVersion { get; init; } = "1.0";

    public string ToMarkdown()
    {
        static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        var text = new StringBuilder("## Repository Constraints\n");
        text.AppendLine($"**Project:** {Cell(Project)} | **Status:** {Status} | **Coverage:** {Coverage}");
        text.AppendLine($"**Generation:** {Generation?.ToString() ?? "unknown"} | **Latest reserved:** {LatestGeneration?.ToString() ?? "unknown"}");
        if (TargetPath is not null) text.AppendLine($"**Target:** {Cell(TargetPath)} ({Evaluation?.TargetStatus ?? "unobserved"})");
        if (PolicyPath is not null) text.AppendLine($"**Policy:** {Cell(PolicyPath)} | **Fingerprint:** {PolicyFingerprint}");
        foreach (var diagnostic in Diagnostics.Concat(Evaluation?.Diagnostics ?? []))
            text.AppendLine($"- {Cell(diagnostic.Location)}: {Cell(diagnostic.Code)} — {Cell(diagnostic.Reason)}");
        if (Evaluation is null) return text.ToString();
        foreach (var rule in Evaluation.Rules)
        {
            text.AppendLine($"\n### {Cell(rule.RuleId)} ({rule.Family}, {rule.Severity})");
            text.AppendLine($"{rule.Status}; coverage={rule.Coverage}; evaluated files={rule.EvaluatedFiles}; findings={rule.FindingCount}; truncated={rule.FindingsTruncated.ToString().ToLowerInvariant()}");
            text.AppendLine(Cell(rule.Reason));
            foreach (var finding in rule.Findings)
                text.AppendLine($"- {Cell(finding.Path)}{(finding.Line is { } line ? $":{line}:{finding.Column}" : "")}: {finding.Status} — {Cell(finding.Reason)}");
        }
        return text.ToString();
    }
}
