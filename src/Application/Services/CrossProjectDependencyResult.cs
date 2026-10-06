using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed record CrossProjectDependencyResult(
    string ContractVersion, string? ProjectContext,
    IReadOnlyList<CrossProjectEdgeResult> Edges, IReadOnlyList<PackageDependencyResult> PackageReferences, bool Truncated)
{
    public string ToMarkdown()
    {
        if (Edges.Count == 0 && PackageReferences.Count == 0)
            return $"No cross-project dependencies found{(ProjectContext is not null ? $" involving '{ProjectContext}'" : "")}. All edges appear to be within single projects.";
        var builder = new StringBuilder();
        builder.AppendLine($"## Cross-Project Dependencies{(ProjectContext is not null ? $" — {ProjectContext}" : "")}");
        if (Edges.Count > 0)
        {
            builder.AppendLine($"**{Edges.Count}** edges cross project boundaries:\n");
            builder.AppendLine("| From Project | Source | Rel | Target | To Project |");
            builder.AppendLine("|-------------|--------|-----|--------|-----------|");
            foreach (var edge in Edges)
                builder.AppendLine($"| `{Escape(edge.Source.ProjectContext)}` | `{Escape(edge.Source.Name)}` ({edge.Source.Type}) | {edge.Relationship} | `{Escape(edge.Target.Name)}` ({edge.Target.Type}) | `{Escape(edge.Target.ProjectContext)}` |");
        }
        if (PackageReferences.Count > 0)
        {
            builder.AppendLine("\n### Compiled dependencies and source associations");
            builder.AppendLine("Current-source associations support navigation and potential impact; they are not verified calls to the installed implementation.\n");
            builder.AppendLine("| Consumer / site | Package / installed version | Framework | Symbol / relationship | Producer / source version | Source | Status / reason |");
            builder.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var dependency in PackageReferences)
                builder.AppendLine($"| `{Escape(dependency.ProjectContext)}` `{Escape(dependency.Reference.FilePath)}:{dependency.Reference.Line}` | `{Escape(dependency.Reference.PackageId ?? dependency.Reference.AssemblyName)}` `{Escape(dependency.Reference.PackageVersion)}` | `{Escape(dependency.Scope.TargetFramework)}` | `{Escape(dependency.Reference.SymbolKey)}` {dependency.Reference.Relationship} | `{Escape(dependency.Resolution.ProducerProject)}` `{Escape(dependency.Resolution.ProducerVersion)}` | `{Escape(dependency.Target?.FilePath)}:{dependency.Target?.LineNumber}` | {Escape(dependency.Resolution.Status)}: {Escape(dependency.Resolution.Reason)} |");
        }
        if (Truncated) builder.AppendLine("\nResults are bounded; narrow the project context for more focused results.");
        return builder.ToString();
    }

    private static string Escape(string? value) => (value ?? "—").Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("`", "'", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
}
