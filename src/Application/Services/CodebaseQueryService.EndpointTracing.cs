using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> TraceEndpointAsync(
        string route,
        string? projectContext = null,
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var paths = await codeGraph.FindEndpointTracesAsync(route, projectContext, cancellationToken: cancellationToken);
        if (paths.Count == 0)
            return $"No database or event trace found for `{route}`{(projectContext is not null ? $" in '{projectContext}'" : "")}. Re-index after enabling `.meridian/database-tracing.json` if database paths are expected.";

        var rankedPaths = paths
            .Where(path => path.Steps.Count > 1)
            .OrderBy(path => path.Steps.Count)
            .ThenBy(path => path.Steps[^1].Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var databasePaths = rankedPaths.Where(IsDatabaseTerminal).ToArray();
        var eventPaths = rankedPaths.Where(path => path.Steps[^1].Node.Type == CodeNodeType.MessageTopic).ToArray();

        if (detailLevel == ContextDetailLevel.Summary)
            return $"Endpoint trace summary for `{route}`: {databasePaths.Length} database path(s), {eventPaths.Length} event path(s), {rankedPaths.Length} total graph path(s).";

        var sb = new StringBuilder();
        sb.AppendLine($"## Endpoint Trace - `{route}`{(projectContext is not null ? $" - {projectContext}" : string.Empty)}");
        sb.AppendLine("Graph-only trace across indexed route, structural, database, and messaging edges.");
        sb.AppendLine();

        AppendPathSection(sb, "Database paths", databasePaths);
        AppendPathSection(sb, "Event paths", eventPaths);

        sb.AppendLine("> Database paths require indexed `DatabaseOperation` and `DatabaseTable` concepts from the Roslyn database tracing pipeline. Messaging paths depend on existing `PublishesTo` or `SubscribesTo` graph edges.");
        return sb.ToString();
    }

    private static void AppendPathSection(StringBuilder sb, string title, IReadOnlyList<EndpointTracePath> paths)
    {
        sb.AppendLine($"### {title} ({paths.Count})");
        if (paths.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        foreach (var path in paths)
        {
            var terminal = path.Steps[^1].Node;
            sb.AppendLine($"- **{terminal.Type}** `{terminal.Name}`");
            foreach (var step in path.Steps)
            {
                sb.Append($"  - **{step.Node.Type}** `{step.Node.Name}`");
                if (!string.IsNullOrWhiteSpace(step.Node.FilePath))
                    sb.Append($" ({step.Node.FilePath})");
                if (!string.IsNullOrWhiteSpace(step.RelationshipType))
                    sb.Append($" -[{step.RelationshipType}]->");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
    }

    private static bool IsDatabaseTerminal(EndpointTracePath path)
    {
        var terminal = path.Steps[^1].Node;
        return terminal.Type == CodeNodeType.DatabaseTable
               || terminal.Properties.TryGetValue("externalKind", out var kind)
               && string.Equals(kind, "DatabaseTable", StringComparison.OrdinalIgnoreCase);
    }
}
