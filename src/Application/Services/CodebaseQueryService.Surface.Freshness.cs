using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> CheckGraphFreshnessAsync(
        string? query = null,
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = string.IsNullOrWhiteSpace(query) ? null : query,
                ProjectContext = projectContext,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken);

        if (nodes.Count == 0)
        {
            var projectHint = await BuildProjectContextHintAsync(projectContext, cancellationToken);
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}{(query is not null ? $" for `{query}`" : "")}.{projectHint}";
        }

        var checks = nodes.Select(BuildFreshness).ToArray();
        var high = checks.Count(c => c.Confidence == "High");
        var medium = checks.Count(c => c.Confidence == "Medium");
        var low = checks.Count(c => c.Confidence == "Low");

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Freshness{(projectContext is not null ? $" - {projectContext}" : "")}");
        if (!string.IsNullOrWhiteSpace(query))
            sb.AppendLine($"**Query:** `{query}`");
        sb.AppendLine($"**Trust summary:** {high} High, {medium} Medium, {low} Low confidence\n");
        sb.AppendLine("| Confidence | Node | Source verification | Line metadata | Last indexed / content updated | Reason |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var check in checks)
        {
            var indexed = check.Node.LastIndexedAt?.ToString("u") ?? "unknown";
            var updated = check.Node.UpdatedAt?.ToString("u") ?? "unknown";
            sb.AppendLine($"| {check.Confidence} | `{check.Node.Name}` ({check.Node.Type}) | {check.SourceVerification} | {check.LineMetadata} | indexed {indexed}<br>updated {updated} | {check.Reason} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindGraphDriftAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 1000
            },
            cancellationToken);

        if (nodes.Count == 0)
        {
            var projectHint = await BuildProjectContextHintAsync(projectContext, cancellationToken);
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}.{projectHint} Run the indexer before checking drift.";
        }

        var checks = nodes.Select(BuildFreshness).ToArray();
        var missingFileMetadata = checks.Where(c => string.IsNullOrWhiteSpace(c.Node.FilePath)).ToArray();
        var incompleteLines = checks.Where(c => c.LineMetadata == "incomplete").ToArray();
        var missingTimestamps = checks.Where(c => c.Node.UpdatedAt is null).ToArray();
        var missingSourceHashes = checks
            .Where(c => c.SourceVerification == "missing source hash" && RequiresSourceHash(c.Node))
            .ToArray();
        var lowConfidence = checks.Count(c => c.Confidence == "Low");

        if (missingFileMetadata.Length == 0 && incompleteLines.Length == 0 && missingTimestamps.Length == 0 && missingSourceHashes.Length == 0)
            return $"Graph drift: low{(projectContext is not null ? $" for '{projectContext}'" : "")}. Indexed file metadata, line metadata, source hashes, and update timestamps look consistent. Source files are not read by the MCP server.";

        var severity = lowConfidence > 25 || missingFileMetadata.Length > 50 || incompleteLines.Length > 100 ? "high"
            : lowConfidence > 5 || missingFileMetadata.Length > 10 || incompleteLines.Length > 25 ? "moderate"
            : "low";

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Drift{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**Drift:** {severity}");
        sb.AppendLine($"**Signals:** {missingFileMetadata.Length} nodes lack file paths, {incompleteLines.Length} have incomplete line metadata, {missingSourceHashes.Length} lack source hashes, {missingTimestamps.Length} lack update timestamps.");
        sb.AppendLine("**Source verification:** source hashes are checked for presence; source files are not read by the MCP server because indexed projects may live on a different machine.\n");

        AppendDriftSection(sb, "Missing file metadata", missingFileMetadata, limit);
        AppendDriftSection(sb, "Incomplete line metadata", incompleteLines, limit);
        AppendDriftSection(sb, "Missing source hashes", missingSourceHashes, limit);
        AppendDriftSection(sb, "Missing timestamps", missingTimestamps, limit);

        sb.AppendLine("Recommendation: run `codemeridian index . --project <ProjectName> --clear` with an indexer version that sends `sourceHash` if these nodes are expected to have complete file, line, checksum, and timestamp metadata.");

        return sb.ToString();
    }

    private static FreshnessCheck BuildFreshness(CodeNode node)
    {
        var hasFilePath = !string.IsNullOrWhiteSpace(node.FilePath);
        var hasLineMetadata = node.LineNumber is > 0 || node.LineCount is > 0;
        var hasTimestamp = node.UpdatedAt is not null;
        var hasSourceHash = !string.IsNullOrWhiteSpace(node.SourceHash);
        var requiresSourceHash = RequiresSourceHash(node);
        var lineMetadata = hasLineMetadata ? "present" : "incomplete";
        var sourceVerification = !hasFilePath ? "no file path"
            : hasSourceHash ? "checksum indexed"
            : !requiresSourceHash ? "structural node"
            : "missing source hash";
        var confidence = hasFilePath && hasLineMetadata && hasTimestamp && (hasSourceHash || !requiresSourceHash) ? "High"
            : hasFilePath && hasTimestamp ? "Medium"
            : "Low";
        var reason = confidence switch
        {
            "High" when !requiresSourceHash => "structural node with file, line, and content-update metadata",
            "High" => "indexer supplied file, line, checksum, and content-update metadata",
            "Medium" when requiresSourceHash && !hasSourceHash => "indexer supplied file and update metadata, but source hash is missing",
            "Medium" => "indexer supplied file and update metadata, but line metadata is incomplete",
            _ => !hasFilePath ? "node has no file path" : "node is missing update metadata"
        };

        return new FreshnessCheck(node, sourceVerification, lineMetadata, confidence, reason);
    }

    private static string DescribeFreshness(FreshnessCheck check) =>
        $"{check.Confidence}: {check.Reason}";

    private static bool RequiresSourceHash(CodeNode node) =>
        node.Type is not CodeNodeType.Namespace and not CodeNodeType.Diagnostic;

    private static void AppendDriftSection(StringBuilder sb, string title, IReadOnlyCollection<FreshnessCheck> checks, int limit)
    {
        if (checks.Count == 0)
            return;

        sb.AppendLine($"### {title} ({checks.Count})");
        foreach (var check in checks.Take(Math.Clamp(limit, 1, 100)))
            sb.AppendLine($"- `{check.Node.Name}` ({check.Node.Type}) - `{check.Node.FilePath ?? "no file"}` - {check.Reason}");
        sb.AppendLine();
    }

    private sealed record FreshnessCheck(
        CodeNode Node,
        string SourceVerification,
        string LineMetadata,
        string Confidence,
        string Reason);
}
