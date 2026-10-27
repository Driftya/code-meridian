using System.Text;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Application.Projects;

public sealed record ProjectProfileResult(
    string Status,
    string Project,
    long? Generation,
    long? LatestGeneration,
    DateTimeOffset? PublishedAt,
    ProjectProfile? Profile,
    ProjectProfileTarget? Target,
    IReadOnlyList<string> Warnings)
{
    public string ContractVersion { get; init; } = "1.0";
    public IReadOnlyList<ProjectAnalyzerOutcome> AnalyzerOutcomes { get; init; } = [];
    public RepositoryConstraints.RepositoryConstraintCapabilitySummary? RepositoryConstraints { get; init; }

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.AppendLine($"## Project Profile — `{Project}`");
        text.AppendLine($"**Status:** {Status}");
        if (Profile is null)
        {
            text.AppendLine("No complete discovery profile has been published. Project composition and indexing readiness are unknown.");
            text.AppendLine("Publish discovery with `codemeridian profile . --publish`; do not infer a documentation-only project from an empty graph.");
            return text.ToString();
        }
        text.AppendLine($"**Generation:** {Generation} | **Latest reserved:** {LatestGeneration}");
        text.AppendLine($"**Observed:** {Profile.ObservedAt:O} | **Published:** {PublishedAt:O}");
        text.AppendLine($"**Revision:** {Profile.Revision?.Commit ?? "unknown"} | **Dirty at discovery:** {Profile.Revision?.Dirty?.ToString().ToLowerInvariant() ?? "unknown"}");
        text.AppendLine($"**Configuration fingerprint:** {Profile.ConfigurationFingerprint ?? "unknown"}");
        text.AppendLine($"**Source fingerprint:** {Profile.SourceFingerprint ?? "unknown"}");
        text.AppendLine($"**Files:** {Profile.FileCount} | **Root records:** {Profile.AnalysisRoots.Count} of {Profile.AnalysisRootCount}");
        text.AppendLine("Discovery and analyzer completion are separate facts. Readiness requires successful coverage of the requested scope.");
        text.AppendLine();
        text.AppendLine("| File kind | Count | Examples |");
        text.AppendLine("|---|---:|---|");
        foreach (var kind in Profile.FileKinds)
            text.AppendLine($"| {kind.Kind} | {kind.Count} | {string.Join(", ", kind.ExamplePaths).Replace("|", "\\|")} |");
        if (Target is not null)
            text.AppendLine($"**Target:** `{Target.Path}` — {Target.Status}" + (Target.Kind is null ? "" : $" ({Target.Kind})"));
        foreach (var analyzer in Profile.Analyzers)
        {
            text.AppendLine($"- {analyzer.Id}: support={(analyzer.Supported ? "supported" : "unsupported")}, configuration={analyzer.ConfigurationState}, indexing={analyzer.IndexingState}");
            text.AppendLine(analyzer.Fingerprint is { } fingerprint
                ? $"  Implementation fingerprint: {fingerprint.Implementation}; parser fingerprint: {fingerprint.Parser}"
                : "  Implementation/parser fingerprints: unknown");
        }
        foreach (var outcome in AnalyzerOutcomes)
            text.AppendLine($"- {outcome.Analyzer} scope `{outcome.Scope}`: state={outcome.State}, coverage={outcome.Coverage}, mode={outcome.Mode}, source={outcome.SourceFingerprint ?? "unknown"}");
        if (RepositoryConstraints is { } constraints)
        {
            text.AppendLine($"- Repository constraints: status={constraints.Status}, coverage={constraints.Coverage}; families={string.Join(", ", constraints.RuleFamilies)}");
            foreach (var reason in constraints.Reasons) text.AppendLine($"  {reason}");
        }
        foreach (var warning in Warnings) text.AppendLine($"- {warning}");
        return text.ToString();
    }
}
