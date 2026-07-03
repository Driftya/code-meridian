namespace CodeMeridian.Application.Services;

internal static class ToolDependencyCatalog
{
    public static IReadOnlyDictionary<string, ToolDependencySubject> Subjects { get; } = BuildSubjects();

    public static IReadOnlyList<ToolDependencyEdge> Edges { get; } =
    [
        Edge(
            "suggest_extractions",
            "plan_context_workflow",
            "extraction-planning workflow guidance",
            "awareness",
            "Workflow recipes recommend suggest_extractions as one comparison signal for refactor planning, so planner guidance should stay aligned with extraction confidence and location semantics.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs"
            ],
            [
                "docs/features.md",
                "docs/features/29-add-refactor-extraction-candidates.md",
                "docs/features/43-add-context-workflow-planning.md"
            ]),
        Edge(
            "plan_context_workflow",
            "execute_context_workflow",
            "workflow plan shape",
            "hard",
            "The executor consumes planned step order, warning semantics, and optional-step behavior from the workflow planner.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs",
                "tests/CodeMeridian.McpServer.Tests/KnowledgeIngestionTests.cs"
            ],
            [
                "docs/context-workflows.md",
                "docs/features.md",
                "docs/features/43-add-context-workflow-planning.md",
                "docs/features/52-prune-optional-context-workflow-steps.md"
            ]),
        Edge(
            "find_test_shield",
            "build_minimal_context",
            "test-target ranking and verification categories",
            "hard",
            "Context packs reuse the same focused verification story, so shield ranking and context-pack test guidance must stay aligned.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/features/46-add-slice-aware-test-shield-ranking.md",
                "docs/features/50-add-context-pack-test-recommendation-pruning.md"
            ]),
        Edge(
            "find_test_shield",
            "pr_context_report",
            "missing-test and verification-risk semantics",
            "awareness",
            "PR review summaries surface missing-test risk, so major shielding changes can shift what reviewers should verify even without a direct method call dependency.",
            [
                "tests/CodeMeridian.Application.Tests/Services/PrContextReportServiceTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/features/21-add-ci-friendly-context-reports.md"
            ]),
        Edge(
            "find_test_shield",
            "plan_context_workflow",
            "focused verification workflow guidance",
            "awareness",
            "Workflow recipes use find_test_shield as the verification-planning step, so ranking and section changes should stay aligned with before-edit and refactor guidance.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/features/43-add-context-workflow-planning.md",
                "docs/features/46-add-slice-aware-test-shield-ranking.md",
                "docs/features/52-prune-optional-context-workflow-steps.md"
            ]),
        Edge(
            "find_related_knowledge",
            "pr_context_report",
            "related-document scoring and lexical confidence thresholds",
            "awareness",
            "PR related-doc output is not a direct wrapper, but it should stay directionally aligned with keyword-confidence pruning and duplicate suppression.",
            [
                "tests/CodeMeridian.Application.Tests/Services/KeywordGraphServiceTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/PrContextReportServiceTests.cs"
            ],
            [
                "docs/features/21-add-ci-friendly-context-reports.md",
                "docs/features/51-prune-related-knowledge-result-noise.md"
            ]),
        Edge(
            "find_related_knowledge",
            "plan_context_workflow",
            "workflow recipe guidance for lexical discovery",
            "awareness",
            "Multiple workflow recipes recommend find_related_knowledge when structural graph links are weak, so planner guidance should stay aligned with lexical-confidence and awareness-only result semantics.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/KeywordGraphServiceTests.cs"
            ],
            [
                "docs/context-workflows.md",
                "docs/features/43-add-context-workflow-planning.md",
                "docs/features/51-prune-related-knowledge-result-noise.md"
            ]),
        Edge(
            "find_implementation_patterns",
            "plan_context_workflow",
            "semantic-discovery workflow guidance",
            "awareness",
            "Semantic-discovery workflows now use structural pattern search as a reusable-example step, so planner guidance should stay aligned with structural evidence and confidence semantics.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/context-workflows.md",
                "docs/features.md",
                "docs/features/43-add-context-workflow-planning.md",
                "docs/features/54-add-structural-implementation-pattern-search.md"
            ]),
        Edge(
            "find_implementation_surface",
            "plan_edit_route",
            "goal-to-target ranking heuristics",
            "awareness",
            "Change-route planning starts from the same vague-goal targeting problem, so confidence labels, pruning, and candidate ranking changes should stay aligned with route anchor selection.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/features/11-find-implementation-surface.md",
                "docs/features/24-add-change-route-planning.md",
                "docs/features/40-add-implementation-surface-pruning.md"
            ]),
        Edge(
            "find_implementation_surface",
            "resolve_exact_symbol",
            "candidate-to-symbol handoff guidance",
            "awareness",
            "Implementation-surface results advertise canonical IDs and target confidence, and workflow guidance expects resolve_exact_symbol to follow when the selected surface is not exact.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceContextWorkflowTests.cs"
            ],
            [
                "docs/features/11-find-implementation-surface.md",
                "docs/features/14-improve-exact-symbol-resolution.md",
                "docs/features/43-add-context-workflow-planning.md"
            ]),
        Edge(
            "build_minimal_context",
            "plan_edit_route",
            "route follow-up context guidance",
            "awareness",
            "Change-route output explicitly tells callers to run build_minimal_context on exact route targets, so route guidance should stay aligned with the bounded context step that follows planning.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/features/01-add-build-minimal-context.md",
                "docs/features/24-add-change-route-planning.md",
                "docs/context-workflows.md"
            ]),
        Edge(
            "build_minimal_context",
            "evaluate_session",
            "contextPackStatus result contract",
            "hard",
            "Session evaluation counts full, degraded, and failed context-pack outcomes from build_minimal_context tool-result events.",
            [
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs",
                "tests/CodeMeridian.Indexer.Tests/Cli/SessionUsefulnessEvaluatorTests.cs"
            ],
            [
                "docs/evaluate.md",
                "docs/features/44-add-context-pack-degraded-mode.md"
            ]),
        Edge(
            "session_evidence_format",
            "evaluate_session",
            "session evidence JSONL schema",
            "hard",
            "The evaluator depends on the provider-neutral evidence shape, including toolName, files, tests, stale warnings, contextPackStatus, and optional derived-lineage fields.",
            [
                "tests/CodeMeridian.Indexer.Tests/Cli/SessionUsefulnessEvaluatorTests.cs"
            ],
            [
                "docs/evaluate.md",
                "docs/agent-capabilities/skills/codemeridian-context/SKILL.md",
                "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md"
            ]),
        Edge(
            "evaluate_session",
            "find_implementation_surface",
            "precision-feedback output shape",
            "hard",
            "Implementation-surface ranking reads .meridian/precision-feedback.json written by evaluate-session to explain direct, derived, and ignored target history.",
            [
                "tests/CodeMeridian.Indexer.Tests/Cli/SessionUsefulnessEvaluatorTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/evaluate.md",
                "docs/features/39-add-precision-feedback-loop.md",
                "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md"
            ]),
        Edge(
            "evaluate_session",
            "analyze_feature_implementation_path",
            "precision-feedback output shape",
            "hard",
            "Feature-path analysis also reads evaluate-session precision feedback to explain why certain surfaces were accepted directly, accepted by derivation, or ignored before.",
            [
                "tests/CodeMeridian.Indexer.Tests/Cli/SessionUsefulnessEvaluatorTests.cs",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            [
                "docs/evaluate.md",
                "docs/features/39-add-precision-feedback-loop.md",
                "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md"
            ])
    ];

    public static ToolDependencySubject? FindSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var normalized = Normalize(subject);
        return Subjects.Values.FirstOrDefault(candidate =>
            candidate.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || candidate.Aliases.Any(alias => alias.Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyDictionary<string, ToolDependencySubject> BuildSubjects() =>
        new[]
        {
            Subject("plan_context_workflow", "Tool", "Plan Context Workflow", "MCP/application workflow planner for ordered CodeMeridian tool recipes.", ["mcp__codemeridian.plan_context_workflow"]),
            Subject("execute_context_workflow", "Tool", "Execute Context Workflow", "MCP/application workflow executor for approved read-only workflow steps.", ["mcp__codemeridian.execute_context_workflow"]),
            Subject("find_test_shield", "Tool", "Find Test Shield", "Graph test-shield tool for direct, indirect, and unshielded verification paths.", ["mcp__codemeridian.find_test_shield"]),
            Subject("build_minimal_context", "Tool", "Build Minimal Context", "Bounded context-pack tool for editing and review.", ["mcp__codemeridian.build_minimal_context"]),
            Subject("find_related_knowledge", "Tool", "Find Related Knowledge", "Keyword-driven related-doc and related-code discovery tool.", ["mcp__codemeridian.find_related_knowledge"]),
            Subject("find_implementation_patterns", "Tool", "Find Implementation Patterns", "Structural implementation-pattern search that blends semantic seeds with graph reranking.", ["mcp__codemeridian.find_implementation_patterns"]),
            Subject("suggest_extractions", "Tool", "Suggest Extractions", "Safe-first extraction candidate tool built from natural modules, hotspot signals, tests, and coverage gaps.", ["mcp__codemeridian.suggest_extractions"]),
            Subject("pr_context_report", "CLI report", "PR Context Report", "codemeridian report pr-context CI summary.", ["codemeridian report pr-context", "report pr-context"]),
            Subject("session_evidence_format", "Contract", "Session Evidence Format", "Provider-neutral .meridian/sessions/*.jsonl schema used by session evaluation.", ["session evidence", "session evidence jsonl", "session-evidence-format"]),
            Subject("evaluate_session", "CLI evaluator", "Evaluate Session", "codemeridian evaluate-session evidence evaluator and precision-feedback writer.", ["codemeridian evaluate-session", "evaluate-session"]),
            Subject("find_implementation_surface", "Tool", "Find Implementation Surface", "Feature/fix targeting tool that ranks likely files and symbols.", ["mcp__codemeridian.find_implementation_surface"]),
            Subject("analyze_feature_implementation_path", "Tool", "Analyze Feature Implementation Path", "Feature-doc and goal mapper that summarizes implementation status, surfaces, and tests.", ["mcp__codemeridian.analyze_feature_implementation_path"]),
            Subject("plan_edit_route", "Tool", "Plan Edit Route", "Ordered change-route planner across contracts, behavior, infrastructure, composition, and tests.", ["mcp__codemeridian.plan_edit_route", "plan-edit"]),
            Subject("resolve_exact_symbol", "Tool", "Resolve Exact Symbol", "Canonical node resolver for symbol, file, and line hints before exact graph traversal.", ["mcp__codemeridian.resolve_exact_symbol"])
        }.ToDictionary(subject => subject.Id, StringComparer.OrdinalIgnoreCase);

    private static ToolDependencySubject Subject(
        string id,
        string kind,
        string displayName,
        string description,
        IReadOnlyList<string> aliases) =>
        new(Normalize(id), kind, displayName, description, aliases.Select(Normalize).ToArray());

    private static ToolDependencyEdge Edge(
        string producerId,
        string consumerId,
        string contractType,
        string impactLevel,
        string reason,
        IReadOnlyList<string> regressionSuites,
        IReadOnlyList<string> reviewArtifacts) =>
        new(
            Normalize(producerId),
            Normalize(consumerId),
            contractType,
            impactLevel,
            reason,
            regressionSuites,
            reviewArtifacts);

    private static string Normalize(string value) =>
        value.Trim().Replace("CodeMeridian.", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
}

internal sealed record ToolDependencySubject(
    string Id,
    string Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases);

internal sealed record ToolDependencyEdge(
    string ProducerId,
    string ConsumerId,
    string ContractType,
    string ImpactLevel,
    string Reason,
    IReadOnlyList<string> RegressionSuites,
    IReadOnlyList<string> ReviewArtifacts);
