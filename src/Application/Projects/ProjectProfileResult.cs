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
        text.AppendLine($"**Files:** {Profile.FileCount} | **Root records:** {Profile.AnalysisRoots.Count} of {Profile.AnalysisRootCount}");
        text.AppendLine("Semantic indexing readiness remains unknown; published discovery is not evidence that analyzers completed.");
        text.AppendLine();
        text.AppendLine("| File kind | Count | Examples |");
        text.AppendLine("|---|---:|---|");
        foreach (var kind in Profile.FileKinds)
            text.AppendLine($"| {kind.Kind} | {kind.Count} | {string.Join(", ", kind.ExamplePaths).Replace("|", "\\|")} |");
        if (Target is not null)
            text.AppendLine($"**Target:** `{Target.Path}` — {Target.Status}" + (Target.Kind is null ? "" : $" ({Target.Kind})"));
        foreach (var analyzer in Profile.Analyzers)
            text.AppendLine($"- {analyzer.Id}: support={(analyzer.Supported ? "supported" : "unsupported")}, configuration={analyzer.ConfigurationState}, indexing={analyzer.IndexingState}");
        foreach (var warning in Warnings) text.AppendLine($"- {warning}");
        return text.ToString();
    }
}
