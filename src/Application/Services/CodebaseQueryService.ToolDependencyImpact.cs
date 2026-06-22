using System.Text;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<string> FindToolDependencyImpactAsync(
        string? subject = null,
        bool includeAwarenessOnly = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(subject)
            ? FormatDependencyMatrix(includeAwarenessOnly)
            : FormatDependencySubject(subject, includeAwarenessOnly));
    }

    private static string FormatDependencyMatrix(bool includeAwarenessOnly)
    {
        var edges = SelectEdges(includeAwarenessOnly);
        var hiddenAwarenessCount = ToolDependencyCatalog.Edges.Count(edge => IsAwarenessOnly(edge) && !includeAwarenessOnly);

        var sb = new StringBuilder();
        sb.AppendLine("## Tool Dependency Impact Matrix");
        sb.AppendLine($"Tracked subjects: **{ToolDependencyCatalog.Subjects.Count}**");
        sb.AppendLine($"Dependency edges shown: **{edges.Count}**");
        if (hiddenAwarenessCount > 0)
            sb.AppendLine($"Awareness-only edges hidden by default: **{hiddenAwarenessCount}**. Re-run with `includeAwarenessOnly=true` when reviewing softer alignment risks.");
        sb.AppendLine();
        sb.AppendLine("| Producer | Consumer | Contract | Impact |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var edge in edges)
        {
            var producer = ToolDependencyCatalog.Subjects[edge.ProducerId];
            var consumer = ToolDependencyCatalog.Subjects[edge.ConsumerId];
            sb.AppendLine($"| `{producer.Id}` | `{consumer.Id}` | {edge.ContractType} | {Capitalize(edge.ImpactLevel)} |");
        }

        sb.AppendLine();
        sb.AppendLine("Use `find_tool_dependency_impact` with a specific tool, report, evaluator, or contract name when you need the exact downstream consumers, upstream contracts, regression suites, and docs to review.");

        return sb.ToString();
    }

    private static string FormatDependencySubject(string subject, bool includeAwarenessOnly)
    {
        var match = ToolDependencyCatalog.FindSubject(subject);
        if (match is null)
        {
            var supported = string.Join(", ", ToolDependencyCatalog.Subjects.Values
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"`{item.Id}`"));
            return $"No tracked tool dependency subject matched `{subject}`. Supported subjects: {supported}.";
        }

        var outgoing = SelectEdges(includeAwarenessOnly)
            .Where(edge => edge.ProducerId.Equals(match.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var incoming = SelectEdges(includeAwarenessOnly)
            .Where(edge => edge.ConsumerId.Equals(match.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hiddenAwarenessCount = ToolDependencyCatalog.Edges.Count(edge =>
            (edge.ProducerId.Equals(match.Id, StringComparison.OrdinalIgnoreCase)
             || edge.ConsumerId.Equals(match.Id, StringComparison.OrdinalIgnoreCase))
            && IsAwarenessOnly(edge)
            && !includeAwarenessOnly);

        var sb = new StringBuilder();
        sb.AppendLine($"## Tool Dependency Impact - `{match.Id}`");
        sb.AppendLine($"**Kind:** {match.Kind}");
        sb.AppendLine($"**Description:** {match.Description}");
        if (hiddenAwarenessCount > 0)
            sb.AppendLine($"**Hidden awareness-only edges:** {hiddenAwarenessCount}. Set `includeAwarenessOnly=true` to review softer alignment risks.");
        sb.AppendLine();

        AppendEdgeSection(sb, "Downstream Consumers", outgoing, edge => edge.ConsumerId);
        AppendEdgeSection(sb, "Upstream Dependencies", incoming, edge => edge.ProducerId);

        var regressionSuites = outgoing
            .Concat(incoming)
            .SelectMany(edge => edge.RegressionSuites)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reviewArtifacts = outgoing
            .Concat(incoming)
            .SelectMany(edge => edge.ReviewArtifacts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        sb.AppendLine("### Regression Suites");
        if (regressionSuites.Length == 0)
        {
            sb.AppendLine("- No tracked regression suites yet.");
        }
        else
        {
            foreach (var suite in regressionSuites)
                sb.AppendLine($"- `{suite}`");
        }

        sb.AppendLine();
        sb.AppendLine("### Review Artifacts");
        if (reviewArtifacts.Length == 0)
        {
            sb.AppendLine("- No tracked docs or review artifacts yet.");
        }
        else
        {
            foreach (var artifact in reviewArtifacts)
                sb.AppendLine($"- `{artifact}`");
        }

        return sb.ToString();
    }

    private static void AppendEdgeSection(
        StringBuilder sb,
        string heading,
        IReadOnlyList<ToolDependencyEdge> edges,
        Func<ToolDependencyEdge, string> relatedSubjectIdSelector)
    {
        sb.AppendLine($"### {heading}");
        if (edges.Count == 0)
        {
            sb.AppendLine("- No tracked dependencies.");
            sb.AppendLine();
            return;
        }

        foreach (var edge in edges
                     .OrderBy(edge => ImpactSort(edge.ImpactLevel))
                     .ThenBy(edge => ToolDependencyCatalog.Subjects[relatedSubjectIdSelector(edge)].Id, StringComparer.OrdinalIgnoreCase))
        {
            var related = ToolDependencyCatalog.Subjects[relatedSubjectIdSelector(edge)];
            sb.AppendLine($"- `{related.Id}` ({related.Kind}, {Capitalize(edge.ImpactLevel)})");
            sb.AppendLine($"  Contract: {edge.ContractType}");
            sb.AppendLine($"  Reason: {edge.Reason}");
        }

        sb.AppendLine();
    }

    private static IReadOnlyList<ToolDependencyEdge> SelectEdges(bool includeAwarenessOnly) =>
        ToolDependencyCatalog.Edges
            .Where(edge => includeAwarenessOnly || !IsAwarenessOnly(edge))
            .OrderBy(edge => ToolDependencyCatalog.Subjects[edge.ProducerId].Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => ToolDependencyCatalog.Subjects[edge.ConsumerId].Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsAwarenessOnly(ToolDependencyEdge edge) =>
        edge.ImpactLevel.Equals("awareness", StringComparison.OrdinalIgnoreCase);

    private static int ImpactSort(string impactLevel) =>
        impactLevel.Equals("hard", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}
