using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public async Task<string> FindConfigDefinitionsAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = await codeGraph.FindConfigDefinitionsAsync(canonicalKey, projectContext, cancellationToken);

        if (definitions.Count == 0)
            return $"No configuration definitions found for `{canonicalKey}`" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : string.Empty)}. " +
                   "Run `codemeridian index` or `codemeridian config rebuild` to populate the configuration graph.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Config Definitions — `{canonicalKey}`");
        sb.AppendLine();
        sb.AppendLine($"**{definitions.Count}** definitions or overrides found{(projectContext is not null ? $" in `{projectContext}`" : string.Empty)}:");
        sb.AppendLine();
        sb.AppendLine("| Relationship | File | Raw key | Source | Value preview |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var definition in definitions)
        {
            sb.AppendLine(
                $"| `{definition.RelationshipType}` | `{definition.FileNode.FilePath ?? definition.FileNode.Name}` | `{definition.RawKey ?? definition.EntryNode.Name}` | `{definition.SourceKind ?? "unknown"}` | `{EscapeTableCell(definition.ValuePreview ?? string.Empty)}` |");
        }

        return sb.ToString();
    }

    public async Task<string> FindConfigUsageAsync(
        string canonicalKey,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var usages = await codeGraph.FindConfigUsageAsync(canonicalKey, projectContext, cancellationToken);

        if (usages.Count == 0)
            return $"No configuration usage found for `{canonicalKey}`" +
                   $"{(projectContext is not null ? $" in '{projectContext}'" : string.Empty)}. " +
                   "Run `codemeridian index` after the latest Roslyn configuration extraction changes to capture `ReadsConfig` and `BindsConfig` edges.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Config Usage — `{canonicalKey}`");
        sb.AppendLine();
        sb.AppendLine($"**{usages.Count}** usage edges found{(projectContext is not null ? $" in `{projectContext}`" : string.Empty)}:");
        sb.AppendLine();
        sb.AppendLine("| Relationship | Consumer | File | Pattern | Raw key | Confidence |");
        sb.AppendLine("|---|---|---|---|---|---:|");

        foreach (var usage in usages)
        {
            var pattern = usage.AccessPattern ?? usage.RelationshipType;
            if (!string.IsNullOrWhiteSpace(usage.OptionsType))
                pattern += $" ({usage.OptionsType})";

            sb.AppendLine(
                $"| `{usage.RelationshipType}` | `{usage.ConsumerNode.Name}` | `{usage.ConsumerNode.FilePath ?? "—"}` | `{EscapeTableCell(pattern)}` | `{usage.RawKey ?? canonicalKey}` | {usage.Confidence?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"} |");
        }

        return sb.ToString();
    }
}
