using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> CheckGraphFreshnessAsync(
        string? query = null,
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default) =>
        (await CheckGraphFreshnessResultAsync(query, projectContext, limit, cancellationToken)).ToMarkdown();

    public async Task<GraphFreshnessResult> CheckGraphFreshnessResultAsync(
        string? query = null,
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        var queriedNodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = string.IsNullOrWhiteSpace(query) ? null : query,
                ProjectContext = projectContext,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken);
        var nodes = queriedNodes.Where(node => !IsRelationshipIndexRun(node)).ToArray();

        if (nodes.Length == 0)
        {
            var projectHint = await BuildProjectContextHintAsync(projectContext, cancellationToken);
            return new GraphFreshnessResult(
                "1.0",
                projectContext,
                query,
                false,
                0,
                0,
                0,
                null,
                [],
                false,
                projectHint);
        }

        var checks = nodes.Select(BuildFreshness).ToArray();
        var relationshipTrust = await GetRelationshipTrustAsync(projectContext, cancellationToken);
        var high = checks.Count(c => c.Confidence == "High");
        var medium = checks.Count(c => c.Confidence == "Medium");
        var low = checks.Count(c => c.Confidence == "Low");

        const int maximumFindings = 200;
        var findings = checks
            .Take(maximumFindings)
            .Select(check => new GraphFreshnessFindingResult(
                GraphNodeResult.FromNode(check.Node),
                check.Confidence,
                check.SourceVerification,
                check.LineMetadata,
                check.Node.LastIndexedAt,
                check.Node.UpdatedAt,
                check.Reason))
            .ToArray();

        return new GraphFreshnessResult(
            "1.0",
            projectContext,
            query,
            true,
            high,
            medium,
            low,
            ToResult(relationshipTrust),
            findings,
            checks.Length > findings.Length,
            null);
    }

    public async Task<string> FindGraphDriftAsync(
        string? projectContext = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        projectContext = await ResolveProjectContextAsync(projectContext, cancellationToken);
        var queriedNodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                Limit = 1000
            },
            cancellationToken);
        var nodes = queriedNodes.Where(node => !IsRelationshipIndexRun(node)).ToArray();

        if (nodes.Length == 0)
        {
            var projectHint = await BuildProjectContextHintAsync(projectContext, cancellationToken);
            return $"No graph nodes found{(projectContext is not null ? $" in '{projectContext}'" : "")}.{projectHint} Run the indexer before checking drift.";
        }

        var checks = nodes.Select(BuildFreshness).ToArray();
        var relationshipTrust = await GetRelationshipTrustAsync(projectContext, cancellationToken);
        var missingFileMetadata = checks.Where(c => c.ExpectsFilePath && !c.HasFilePath).ToArray();
        var incompleteLines = checks.Where(c => c.ExpectsLineMetadata && !c.HasLineMetadata).ToArray();
        var missingTimestamps = checks.Where(c => !c.HasTimestamp).ToArray();
        var missingSourceHashes = checks.Where(c => c.ExpectsSourceHash && c.HasFilePath && !c.HasSourceHash).ToArray();
        var testRoleConflicts = nodes.Where(HasUnmistakableTestRoleConflict).ToArray();
        var lowConfidence = checks.Count(c => c.Confidence == "Low");

        if (missingFileMetadata.Length == 0
            && incompleteLines.Length == 0
            && missingTimestamps.Length == 0
            && missingSourceHashes.Length == 0
            && testRoleConflicts.Length == 0
            && relationshipTrust.Confidence == "High")
            return $"Graph drift: low{(projectContext is not null ? $" for '{projectContext}'" : "")}. Indexed file metadata, line metadata, source hashes, and update timestamps look consistent. Relationship evidence: {relationshipTrust.Reason}. Outcome totals: {relationshipTrust.UnresolvedLocalCount} unresolved local, {relationshipTrust.IndeterminateCount} indeterminate, {relationshipTrust.ExternalOrUnindexedCount} external/unindexed, {relationshipTrust.DuplicateCount} duplicate candidate(s), {relationshipTrust.SyntheticCount} synthetic edge(s). Source files are not read by the MCP server.";

        var severity = lowConfidence > 25 || missingFileMetadata.Length > 50 || incompleteLines.Length > 100 || testRoleConflicts.Length > 50 ? "high"
            : lowConfidence > 5 || missingFileMetadata.Length > 10 || incompleteLines.Length > 25 || testRoleConflicts.Length > 10 ? "moderate"
            : "low";
        if (relationshipTrust.Confidence == "Low" && severity == "low")
            severity = "moderate";

        var sb = new StringBuilder();
        sb.AppendLine($"## Graph Drift{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**Drift:** {severity}");
        sb.AppendLine($"**Signals:** {missingFileMetadata.Length} nodes lack file paths, {incompleteLines.Length} have incomplete line metadata, {missingSourceHashes.Length} lack source hashes, {missingTimestamps.Length} lack update timestamps, {testRoleConflicts.Length} have stored roles that conflict with unmistakable test paths.");
        sb.AppendLine($"**Relationship completeness:** {relationshipTrust.Confidence} — {relationshipTrust.Reason}");
        AppendRelationshipEvidence(sb, relationshipTrust);
        sb.AppendLine("**Source verification:** source hashes are checked for presence; source files are not read by the MCP server because indexed projects may live on a different machine.\n");

        AppendDriftSection(sb, "Missing file metadata", missingFileMetadata, limit);
        AppendDriftSection(sb, "Incomplete line metadata", incompleteLines, limit);
        AppendDriftSection(sb, "Missing source hashes", missingSourceHashes, limit);
        AppendDriftSection(sb, "Missing timestamps", missingTimestamps, limit);
        if (testRoleConflicts.Length > 0)
        {
            sb.AppendLine($"### Conflicting test file roles ({testRoleConflicts.Length})");
            foreach (var node in testRoleConflicts.Take(Math.Clamp(limit, 1, 100)))
                sb.AppendLine($"- `{node.Name}` ({node.Type}) - `{node.FilePath}` - stored as {node.FileRole}, path classifies as Test");
            sb.AppendLine();
        }

        var hasNodeMetadataDrift = missingFileMetadata.Length > 0
            || incompleteLines.Length > 0
            || missingTimestamps.Length > 0
            || missingSourceHashes.Length > 0
            || testRoleConflicts.Length > 0;
        if (hasNodeMetadataDrift)
        {
            sb.AppendLine("**Node remediation:** run `codemeridian index . --project <ProjectName>` with a current indexer. " +
                          "If the missing IDs, paths, or hashes follow a schema or canonical-ID change, consider a one-time `--clear` rebuild.");
        }

        if (relationshipTrust.Confidence != "High")
        {
            sb.AppendLine("**Relationship remediation:** run a supported non-destructive full relationship index " +
                          "(`codemeridian index . --project <ProjectName>` without `--clear`). " +
                          "Inspect index-run unresolved-reason counts and indexer diagnostics before changing traversal thresholds.");
        }

        return sb.ToString();
    }

    private static FreshnessCheck BuildFreshness(CodeNode node)
    {
        var expectations = GetFreshnessExpectations(node);
        var hasFilePath = !string.IsNullOrWhiteSpace(node.FilePath);
        var hasLineMetadata = node.LineNumber is > 0 || node.LineCount is > 0;
        var hasTimestamp = node.UpdatedAt is not null;
        var hasSourceHash = !string.IsNullOrWhiteSpace(node.SourceHash);
        var lineMetadata = !expectations.RequiresLineMetadata ? "not required"
            : hasLineMetadata ? "present"
            : "incomplete";
        var sourceVerification = !expectations.RequiresFilePath ? "not required"
            : !hasFilePath ? "no file path"
            : !expectations.RequiresSourceHash ? "not required"
            : hasSourceHash ? "checksum indexed"
            : "missing source hash";
        var confidence = !hasTimestamp || (expectations.RequiresFilePath && !hasFilePath) ? "Low"
            : (expectations.RequiresLineMetadata && !hasLineMetadata) || (expectations.RequiresSourceHash && !hasSourceHash) ? "Medium"
            : "Low";
        if (confidence == "Low" && hasTimestamp && (!expectations.RequiresFilePath || hasFilePath))
            confidence = "High";
        var reason = confidence switch
        {
            "High" when !expectations.RequiresFilePath => "structural node with content-update metadata",
            "High" when expectations.RequiresLineMetadata && expectations.RequiresSourceHash => "indexer supplied file, line, checksum, and content-update metadata",
            "High" when expectations.RequiresSourceHash => "indexer supplied file, checksum, and content-update metadata",
            "High" => "indexer supplied the metadata expected for this node type",
            "Medium" when expectations.RequiresSourceHash && !hasSourceHash => "indexer supplied file and update metadata, but source hash is missing",
            "Medium" => "indexer supplied file and update metadata, but required line metadata is incomplete",
            _ => !hasTimestamp ? "node is missing update metadata" : "node has no file path"
        };

        return new FreshnessCheck(
            node,
            sourceVerification,
            lineMetadata,
            confidence,
            reason,
            expectations.RequiresFilePath,
            hasFilePath,
            expectations.RequiresLineMetadata,
            hasLineMetadata,
            expectations.RequiresSourceHash,
            hasSourceHash,
            hasTimestamp);
    }

    private static string DescribeFreshness(FreshnessCheck check) =>
        $"{check.Confidence}: {check.Reason}";

    private static FreshnessExpectations GetFreshnessExpectations(CodeNode node) =>
        node.Type switch
        {
            CodeNodeType.Class or
            CodeNodeType.Struct or
            CodeNodeType.Interface or
            CodeNodeType.Method or
            CodeNodeType.Delegate or
            CodeNodeType.Property or
            CodeNodeType.Field or
            CodeNodeType.Event or
            CodeNodeType.Indexer or
            CodeNodeType.Operator or
            CodeNodeType.Enum => new(true, true, true),
            CodeNodeType.File or
            CodeNodeType.Module or
            CodeNodeType.ConfigurationFile => new(true, false, true),
            CodeNodeType.ConfigurationEntry => new(true, false, false),
            _ => new(false, false, false)
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
        string SourceVerification,
        string LineMetadata,
        string Confidence,
        string Reason,
        bool ExpectsFilePath,
        bool HasFilePath,
        bool ExpectsLineMetadata,
        bool HasLineMetadata,
        bool ExpectsSourceHash,
        bool HasSourceHash,
        bool HasTimestamp);

    private sealed record FreshnessExpectations(
        bool RequiresFilePath,
        bool RequiresLineMetadata,
        bool RequiresSourceHash);
}
