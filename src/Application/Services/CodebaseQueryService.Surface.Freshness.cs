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
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = string.IsNullOrWhiteSpace(query) ? null : query,
                ProjectContext = projectContext,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken);

        if (nodes.Count == 0)
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}{(query is not null ? $" for `{query}`" : "")}.";

        var checks = nodes.Select(BuildFreshness).ToArray();
        var high = checks.Count(c => c.Confidence == "High");
        var medium = checks.Count(c => c.Confidence == "Medium");
        var low = checks.Count(c => c.Confidence == "Low");

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Freshness{(projectContext is not null ? $" - {projectContext}" : "")}");
        if (!string.IsNullOrWhiteSpace(query))
            sb.AppendLine($"**Query:** `{query}`");
        sb.AppendLine($"**Trust summary:** {high} High, {medium} Medium, {low} Low confidence\n");
        sb.AppendLine("| Confidence | Node | File exists | Line valid | Indexed/updated | Reason |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var check in checks)
        {
            var updated = check.Node.UpdatedAt?.ToString("u") ?? "unknown";
            sb.AppendLine($"| {check.Confidence} | `{check.Node.Name}` ({check.Node.Type}) | {YesNo(check.FileExists)} | {YesNo(check.LineRangeStillValid)} | {updated} | {check.Reason} |");
        }

        return sb.ToString();
    }

    public async Task<string> FindGraphDriftAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 1000
            },
            cancellationToken);

        if (nodes.Count == 0)
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}. Run the indexer before checking drift.";

        var checks = nodes.Select(BuildFreshness).ToArray();
        var missingFiles = checks.Where(c => c.Node.FilePath is not null && c.FileExists == false).ToArray();
        var invalidLines = checks.Where(c => c.FileExists == true && c.LineRangeStillValid == false).ToArray();
        var missingTimestamps = checks.Where(c => c.Node.UpdatedAt is null).ToArray();
        var lowConfidence = checks.Count(c => c.Confidence == "Low");

        if (missingFiles.Length == 0 && invalidLines.Length == 0 && missingTimestamps.Length == 0)
            return $"Graph drift: low{(projectContext is not null ? $" for '{projectContext}'" : "")}. Files, line ranges, and update metadata look consistent.";

        var severity = lowConfidence > 25 || missingFiles.Length > 10 ? "high"
            : lowConfidence > 5 || missingFiles.Length > 0 || invalidLines.Length > 5 ? "moderate"
            : "low";

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Drift{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**Drift:** {severity}");
        sb.AppendLine($"**Signals:** {missingFiles.Length} nodes point to missing files, {invalidLines.Length} have invalid line ranges, {missingTimestamps.Length} lack update timestamps.\n");

        AppendDriftSection(sb, "Missing files", missingFiles, limit);
        AppendDriftSection(sb, "Invalid line ranges", invalidLines, limit);
        AppendDriftSection(sb, "Missing timestamps", missingTimestamps, limit);

        if (severity is "moderate" or "high")
            sb.AppendLine("Recommendation: run `codemeridian index . --project <ProjectName> --clear` before relying on exact implementation targets.");

        return sb.ToString();
    }

    private static FreshnessCheck BuildFreshness(CodeNode node)
    {
        var fileExists = FileExists(node.FilePath);
        var lineRangeValid = LineRangeStillValid(node, fileExists);
        var confidence = fileExists == true && lineRangeValid != false ? "High"
            : fileExists == true ? "Medium"
            : "Low";
        var reason = confidence switch
        {
            "High" => "file exists and indexed line metadata is usable",
            "Medium" => "file exists but indexed line metadata looks incomplete or stale",
            _ => string.IsNullOrWhiteSpace(node.FilePath) ? "node has no file path" : "indexed file path was not found"
        };

        return new FreshnessCheck(node, fileExists, lineRangeValid, confidence, reason);
    }

    private static bool? FileExists(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return File.Exists(ResolveRepoPath(filePath));
    }

    private static bool? LineRangeStillValid(CodeNode node, bool? fileExists)
    {
        if (fileExists != true || node.LineNumber is null)
            return null;

        try
        {
            var path = ResolveRepoPath(node.FilePath!);
            var lineCount = File.ReadLines(path).Count();
            return node.LineNumber.Value > 0 && node.LineNumber.Value <= lineCount;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveRepoPath(string filePath)
    {
        if (Path.IsPathRooted(filePath))
            return filePath;

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine(current.FullName, filePath));
            if (File.Exists(candidate))
                return candidate;

            if (File.Exists(Path.Combine(current.FullName, "CodeMeridian.sln")) || Directory.Exists(Path.Combine(current.FullName, ".git")))
                return candidate;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), filePath));
    }

    private static string DescribeFreshness(FreshnessCheck check) =>
        $"{check.Confidence}: {check.Reason}";

    private static string YesNo(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "unknown"
    };

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
        bool? FileExists,
        bool? LineRangeStillValid,
        string Confidence,
        string Reason);
}
