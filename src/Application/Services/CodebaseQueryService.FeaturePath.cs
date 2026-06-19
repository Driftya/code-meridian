using System.Text;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    public async Task<string> AnalyzeFeatureImplementationPathAsync(
        string feature,
        string? projectContext = null,
        bool includeTests = true,
        bool includeDocs = true,
        bool includeRisk = true,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feature))
            return "Provide a feature title, feature request, or docs/features/*.md path.";

        var boundedLimit = Math.Clamp(limit, 1, 50);
        var documents = includeDocs
            ? await vectorStore.SearchByTextAsync(feature, projectContext, topK: 6, cancellationToken)
            : [];
        var keywords = ExtractFeatureKeywords(feature, documents).Take(8).ToArray();
        var queries = new[] { feature }
            .Concat(keywords.Take(5))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidates = new List<CodeNode>();

        foreach (var query in queries)
        {
            var nodes = await codeGraph.QueryNodesAsync(
                new CodeGraphQuery
                {
                    SemanticQuery = query,
                    ProjectContext = projectContext,
                    Limit = Math.Clamp(boundedLimit * 3, 20, 100)
                },
                cancellationToken);

            candidates.AddRange(nodes);
        }

        var ranked = candidates
            .Where(node => !string.IsNullOrWhiteSpace(node.FilePath))
            .DistinctBy(node => node.Id)
            .OrderByDescending(node => ScoreFeaturePathNode(node, feature, keywords))
            .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToArray();

        var relatedTests = includeTests
            ? await FindRelatedFeatureTestsAsync(ranked, projectContext, cancellationToken)
            : [];
        var confidence = DetermineFeaturePathConfidence(feature, documents, ranked, relatedTests);
        var status = DetermineFeatureStatus(documents, ranked, relatedTests);
        var riskLevel = includeRisk ? DetermineFeatureRiskLevel(ranked, relatedTests) : "not_requested";

        var sb = new StringBuilder();
        sb.AppendLine($"## Feature Implementation Path - `{feature}`");
        sb.AppendLine($"**Status:** {status}");
        sb.AppendLine($"**Confidence:** {confidence.Level} - {confidence.Reason}");
        sb.AppendLine($"**Risk level:** {riskLevel}");
        if (keywords.Length > 0)
            sb.AppendLine($"**Signals:** {string.Join(", ", keywords.Take(8).Select(keyword => $"`{keyword}`"))}");
        sb.AppendLine();

        AppendFeatureDocuments(sb, documents);
        AppendFeatureSurfaces(sb, ranked);
        AppendLikelyTouchedAreas(sb, ranked);
        if (includeTests)
            AppendFeatureTestPlan(sb, ranked, relatedTests);
        if (includeDocs)
            AppendFeatureDocsPlan(sb, feature, documents, ranked);
        AppendMissingGraphEvidence(sb, feature, documents, ranked, relatedTests);
        if (includeRisk)
            AppendFeatureRisks(sb, ranked, relatedTests);

        sb.AppendLine();
        sb.AppendLine("CodeMeridian result: this is a graph-backed implementation map, not a claim that the feature is complete. Inspect the listed files before editing and add stronger Feature-node links when the feature is implemented.");

        return sb.ToString();
    }

    private async Task<IReadOnlyList<CodeNode>> FindRelatedFeatureTestsAsync(
        IReadOnlyCollection<CodeNode> ranked,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var tests = new List<CodeNode>();
        foreach (var node in ranked.Take(5))
        {
            var related = await codeGraph.FindRelatedTestsAsync(
                node.Id,
                node.ProjectContext ?? projectContext,
                cancellationToken);
            tests.AddRange(related.Select(item => item.Node));
        }

        tests.AddRange(ranked.Where(IsConfiguredTestNode));
        return tests
            .Where(node => !string.IsNullOrWhiteSpace(node.FilePath))
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ExtractFeatureKeywords(string feature, IReadOnlyCollection<KnowledgeDocument> documents)
    {
        var text = string.Join(
            " ",
            new[] { feature }.Concat(documents.Take(2).Select(document => document.Content)));
        var tokens = text
            .Split([' ', '\t', '\r', '\n', '.', ',', ':', ';', '/', '\\', '-', '_', '`', '"', '\'', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 4)
            .Where(token => !FeatureStopWords.Contains(token))
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .Take(16)
            .ToArray();

        return tokens;
    }

    private int ScoreFeaturePathNode(CodeNode node, string feature, IReadOnlyCollection<string> keywords)
    {
        var score = node.Type switch
        {
            CodeNodeType.Method => 5,
            CodeNodeType.Class or CodeNodeType.Interface => 4,
            CodeNodeType.ApiEndpoint => 4,
            CodeNodeType.File => 2,
            _ => 1
        };

        if (TextMatches(node.Name, feature) || TextMatches(node.Summary, feature))
            score += 6;

        score += keywords.Count(keyword =>
            TextMatches(node.Name, keyword)
            || TextMatches(node.Summary, keyword)
            || TextMatches(node.FilePath, keyword)
            || TextMatches(node.Namespace, keyword)) * 2;

        if (IsContractNode(node) || IsApiNode(node) || IsInfrastructureNode(node))
            score += 2;
        if (node.FileRole == IndexedFileRole.Test || TextMatches(node.FilePath, "test"))
            score -= 2;

        if (!string.IsNullOrWhiteSpace(node.FilePath))
        {
            var feedback = EvaluateSurfaceFeedback("mcp__CodeMeridian.analyze_feature_implementation_path", node.FilePath!);
            score += feedback.ScoreAdjustment;
        }

        return score;
    }

    private static (string Level, string Reason) DetermineFeaturePathConfidence(
        string feature,
        IReadOnlyCollection<KnowledgeDocument> documents,
        IReadOnlyCollection<CodeNode> ranked,
        IReadOnlyCollection<CodeNode> relatedTests)
    {
        var hasFeatureDoc = documents.Any(document => IsFeatureDocumentMatch(document, feature));
        var featureTerms = ExtractFeatureKeywords(feature, []).Take(6).ToArray();
        var directCode = ranked.Any(node =>
            TextMatches(node.Name, feature)
            || TextMatches(node.Summary, feature)
            || featureTerms.Any(term => TextMatches(node.Name, term) || TextMatches(node.Summary, term)));
        var freshCode = ranked.Any(node => BuildFreshness(node).Confidence == "High");

        if (hasFeatureDoc && directCode && relatedTests.Count > 0 && freshCode)
            return ("high", "found a matching feature/document signal, direct code evidence, related tests, and fresh graph metadata");
        if ((hasFeatureDoc || documents.Count > 0) && ranked.Count > 0)
            return ("medium", "found documentation or feature text plus nearby code surfaces, but graph links do not prove completion");
        if (ranked.Count > 0)
            return ("low", "found only fuzzy code surfaces; no strong feature-document evidence was linked");

        return ("low", "no feature document or implementation surface was found in the graph");
    }

    private static string DetermineFeatureStatus(
        IReadOnlyCollection<KnowledgeDocument> documents,
        IReadOnlyCollection<CodeNode> ranked,
        IReadOnlyCollection<CodeNode> relatedTests)
    {
        if (documents.Count == 0 && ranked.Count == 0)
            return "not_found_in_graph";
        if (documents.Count > 0 && ranked.Count == 0)
            return "planned_or_documented_without_code_surface";
        if (documents.Count > 0 && ranked.Count > 0 && relatedTests.Count > 0)
            return "documented_with_code_and_test_evidence";
        if (documents.Count > 0 && ranked.Count > 0)
            return "documented_with_possible_code_surface";
        return "possible_code_surface_without_feature_doc";
    }

    private static string DetermineFeatureRiskLevel(IReadOnlyCollection<CodeNode> ranked, IReadOnlyCollection<CodeNode> relatedTests)
    {
        if (ranked.Count == 0)
            return "unknown";

        var crossesLayers = ranked.Any(IsApiNode) && ranked.Any(IsInfrastructureNode)
            || ranked.Any(IsContractNode) && ranked.Any(IsInfrastructureNode);
        var missingTests = relatedTests.Count == 0;
        var staleTargets = ranked.Count > 0 && ranked.Count(node => BuildFreshness(node).Confidence == "Low") > ranked.Count / 3;

        if (crossesLayers && missingTests || staleTargets)
            return "high";
        if (crossesLayers || missingTests || ranked.Count > 8)
            return "medium";
        return "low";
    }

    private static void AppendFeatureDocuments(StringBuilder sb, IReadOnlyCollection<KnowledgeDocument> documents)
    {
        sb.AppendLine("### Feature/document evidence");
        if (documents.Count == 0)
        {
            sb.AppendLine("- No matching KnowledgeDocument was found.");
            sb.AppendLine();
            return;
        }

        foreach (var document in documents.Take(4))
        {
            var source = document.Source ?? document.Id;
            var status = TryGetMetadata(document, "status") ?? InferDocumentStatus(document.Content);
            sb.AppendLine($"- `{source}`{(status is null ? string.Empty : $" - status `{status}`")}");
        }
        sb.AppendLine();
    }

    private void AppendFeatureSurfaces(StringBuilder sb, IReadOnlyCollection<CodeNode> ranked)
    {
        sb.AppendLine("### Closest implementation surfaces");
        if (ranked.Count == 0)
        {
            sb.AppendLine("- No code surfaces were found.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Area | Symbol | File | Why | Confidence |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var node in ranked.Take(10))
        {
            var area = ClassifyFeatureArea(node);
            var file = node.FilePath is null ? "-" : $"`{node.FilePath}`";
            var why = BuildFeatureSurfaceReason(node);
            var confidence = BuildFreshness(node).Confidence.ToLowerInvariant();
            sb.AppendLine($"| {area} | `{node.Name}` | {file} | {EscapeTableCell(why)} | {confidence} |");
        }
        sb.AppendLine();
    }

    private void AppendLikelyTouchedAreas(StringBuilder sb, IReadOnlyCollection<CodeNode> ranked)
    {
        sb.AppendLine("### Likely touched areas");
        var groups = ranked.GroupBy(ClassifyFeatureArea).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length == 0)
        {
            sb.AppendLine("- Unknown until graph surfaces are linked or indexed.");
            sb.AppendLine();
            return;
        }

        foreach (var group in groups)
        {
            var files = group
                .Select(node => node.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3);
            sb.AppendLine($"- {group.Key}: {string.Join(", ", files.Select(path => $"`{path}`"))}");
        }
        sb.AppendLine();
    }

    private void AppendFeatureTestPlan(StringBuilder sb, IReadOnlyCollection<CodeNode> ranked, IReadOnlyCollection<CodeNode> relatedTests)
    {
        sb.AppendLine("### Tests to add or change");
        if (relatedTests.Count > 0)
        {
            sb.AppendLine("Existing seams:");
            foreach (var test in relatedTests.Take(8))
                sb.AppendLine($"- `{test.Name}` - `{test.FilePath}`");
        }
        else
        {
            sb.AppendLine("Existing seams: none found.");
        }

        var surfaces = ranked.Where(node => !IsConfiguredTestNode(node)).Take(4).ToArray();
        if (surfaces.Length > 0)
        {
            sb.AppendLine("Suggested coverage:");
            foreach (var node in surfaces)
                sb.AppendLine($"- Add focused coverage around `{node.Name}` behavior in `{node.FilePath}`.");
        }
        sb.AppendLine("- Add a contract-level test for the MCP/tool response shape if this feature changes public tool behavior.");
        sb.AppendLine("- Verify CancellationToken flow for any new async graph or document query.");
        sb.AppendLine();
    }

    private static void AppendFeatureDocsPlan(
        StringBuilder sb,
        string feature,
        IReadOnlyCollection<KnowledgeDocument> documents,
        IReadOnlyCollection<CodeNode> ranked)
    {
        sb.AppendLine("### Docs to update");
        var sources = documents
            .Select(document => document.Source ?? document.Id)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (sources.Count == 0 && LooksLikeFeatureDocPath(feature))
            sources.Add(feature);
        if (sources.Count == 0)
            sources.Add("docs/features/<feature>.md");

        foreach (var source in sources)
            sb.AppendLine($"- `{source}`");
        if (ranked.Any(IsApiNode) || ranked.Any(node => TextMatches(node.FilePath, "McpServer")))
            sb.AppendLine("- `README.md` or MCP tool documentation for public tool changes.");
        sb.AppendLine("- `docs/features.md` if the MCP tool list or user-facing capability matrix changes.");
        sb.AppendLine();
    }

    private static void AppendMissingGraphEvidence(
        StringBuilder sb,
        string feature,
        IReadOnlyCollection<KnowledgeDocument> documents,
        IReadOnlyCollection<CodeNode> ranked,
        IReadOnlyCollection<CodeNode> relatedTests)
    {
        sb.AppendLine("### Missing graph evidence");
        if (!documents.Any(document => IsFeatureDocumentMatch(document, feature)))
            sb.AppendLine("- No explicit Feature node or docs/features KnowledgeDocument matched the request strongly.");
        if (ranked.Count == 0)
            sb.AppendLine("- No CodeNode implementation surface matched the feature terms.");
        if (relatedTests.Count == 0)
            sb.AppendLine("- No related test nodes were linked to the closest implementation surfaces.");
        if (!ranked.Any(IsApiNode) && !ranked.Any(node => TextMatches(node.FilePath, "McpServer")))
            sb.AppendLine("- No endpoint or MCP tool exposure node was found.");
        if (ranked.Count > 0 && ranked.Any(node => BuildFreshness(node).Confidence != "High"))
            sb.AppendLine("- Some candidate nodes have incomplete freshness metadata.");
        sb.AppendLine();
    }

    private static void AppendFeatureRisks(StringBuilder sb, IReadOnlyCollection<CodeNode> ranked, IReadOnlyCollection<CodeNode> relatedTests)
    {
        sb.AppendLine("### Risks");
        if (ranked.Count == 0)
        {
            sb.AppendLine("- Risk cannot be estimated until implementation surfaces are indexed or linked.");
            sb.AppendLine();
            return;
        }

        if (ranked.Any(IsContractNode))
            sb.AppendLine("- Contract or port changes may require updates across Application, Infrastructure, Presentation, and tests.");
        if (ranked.Any(IsInfrastructureNode))
            sb.AppendLine("- Infrastructure changes should stay behind Application/Core abstractions and avoid leaking adapter details upward.");
        if (ranked.Any(IsApiNode) || ranked.Any(node => TextMatches(node.FilePath, "McpServer")))
            sb.AppendLine("- Public endpoint/tool changes need stable response shape tests and documentation.");
        if (relatedTests.Count == 0)
            sb.AppendLine("- No linked tests were found; add regression coverage before relying on this path.");
        sb.AppendLine("- Keep ranking/scoring deterministic and testable; avoid hiding planning decisions in repository-specific query code.");
        sb.AppendLine();
    }

    private string BuildFeatureSurfaceReason(CodeNode node)
    {
        var feedback = string.IsNullOrWhiteSpace(node.FilePath)
            ? SurfaceFeedback.None
            : EvaluateSurfaceFeedback("mcp__CodeMeridian.analyze_feature_implementation_path", node.FilePath!);

        string baseReason;
        if (IsContractNode(node))
            baseReason = "contract or port likely shapes the feature boundary";
        else if (IsApiNode(node) || TextMatches(node.FilePath, "McpServer"))
            baseReason = "public endpoint or MCP tool exposure point";
        else if (IsInfrastructureNode(node))
            baseReason = "adapter or persistence surface likely touched by graph/document lookup";
        else if (IsConfiguredTestNode(node))
            baseReason = "test seam close to the candidate implementation";
        else if (IsApplicationNode(node) || IsDomainNode(node))
            baseReason = "application/domain behavior surface";
        else
            baseReason = "keyword or semantic graph match";

        return string.IsNullOrWhiteSpace(feedback.Reason)
            ? baseReason
            : $"{baseReason}; {feedback.Reason}";
    }

    private string ClassifyFeatureArea(CodeNode node)
    {
        if (IsConfiguredTestNode(node))
            return "Tests";
        if (IsApiNode(node) || TextMatches(node.FilePath, "McpServer"))
            return "Presentation/MCP";
        if (IsInfrastructureNode(node))
            return "Infrastructure";
        if (IsContractNode(node) || IsApplicationNode(node))
            return "Application";
        if (IsDomainNode(node))
            return "Core/Domain";
        return "Other";
    }

    private static bool IsFeatureDocumentMatch(KnowledgeDocument document, string feature)
    {
        var source = document.Source ?? document.Id;
        return LooksLikeFeatureDocPath(source)
            || TextMatches(source, feature)
            || TextMatches(document.Content, feature);
    }

    private static bool LooksLikeFeatureDocPath(string value) =>
        value.Contains("docs/features/", StringComparison.OrdinalIgnoreCase)
        || value.Contains("docs\\features\\", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetMetadata(KnowledgeDocument document, string key) =>
        document.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? InferDocumentStatus(string content)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(12))
        {
            if (!line.StartsWith("- Status:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            return separator >= 0 ? line[(separator + 1)..].Trim() : null;
        }

        return null;
    }

    private static readonly HashSet<string> FeatureStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "already",
        "changed",
        "feature",
        "given",
        "implementation",
        "should",
        "tests",
        "their",
        "there",
        "these",
        "this",
        "tool",
        "tools",
        "update",
        "which",
        "with"
    };
}
