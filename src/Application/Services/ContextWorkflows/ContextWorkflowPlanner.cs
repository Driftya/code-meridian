using System.Text.RegularExpressions;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public sealed class ContextWorkflowPlanner
{
    private static readonly IReadOnlyDictionary<string, ContextWorkflowRecipe> RecipeByType = BuildRecipes()
        .ToDictionary(recipe => recipe.WorkflowType, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SupportedWorkflowTypes { get; } =
        RecipeByType.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> MissingRecipeTools { get; } =
        ContextWorkflowToolCatalog.MissingRecipeTools(RecipeByType.Values);

    static ContextWorkflowPlanner()
    {
        if (MissingRecipeTools.Count > 0)
            throw new InvalidOperationException($"Workflow recipes reference unknown tools: {string.Join(", ", MissingRecipeTools)}");
    }

    public ContextWorkflowPlanResult Plan(ContextWorkflowPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return Invalid("Goal is required.", request.ProjectContext, request.Target);

        var workflowType = string.IsNullOrWhiteSpace(request.WorkflowType)
            ? InferWorkflowType(request.Goal, request.Target)
            : request.WorkflowType.Trim();

        if (!RecipeByType.TryGetValue(workflowType, out var recipe))
            return Invalid($"Unsupported workflow type '{workflowType}'. Supported workflow types: {string.Join(", ", SupportedWorkflowTypes)}.", request.ProjectContext, request.Target);

        var maxSteps = Math.Clamp(request.MaxSteps, 1, 25);
        var steps = recipe.Steps
            .Where(step => step.Required || request.IncludeOptionalSteps)
            .Select((step, index) => BuildStep(index + 1, step, request))
            .Take(maxSteps)
            .Select((step, index) => step with { Order = index + 1 })
            .ToArray();

        var warnings = BuildWarnings(recipe, request, steps);
        return new ContextWorkflowPlanResult(
            Status: "valid",
            WorkflowId: recipe.WorkflowType,
            WorkflowType: recipe.WorkflowType,
            Project: NullIfWhiteSpace(request.ProjectContext),
            Target: NullIfWhiteSpace(request.Target),
            Summary: recipe.Summary,
            RequiresApprovalBeforeExecution: steps.Any(step => step.MutatesGraph || step.Destructive),
            EstimatedCost: recipe.EstimatedCost,
            Steps: steps,
            Warnings: warnings,
            FinalResponseGuidance: recipe.FinalResponseGuidance,
            SupportedWorkflowTypes: SupportedWorkflowTypes);
    }

    public static ContextWorkflowRecipe GetRecipe(string workflowType) => RecipeByType[workflowType];

    private static ContextWorkflowPlanResult Invalid(string error, string? projectContext, string? target) =>
        new(
            Status: "invalid",
            WorkflowId: "invalid",
            WorkflowType: "invalid",
            Project: NullIfWhiteSpace(projectContext),
            Target: NullIfWhiteSpace(target),
            Summary: "The requested context workflow could not be planned.",
            RequiresApprovalBeforeExecution: false,
            EstimatedCost: "none",
            Steps: [],
            Warnings: [],
            FinalResponseGuidance: [],
            SupportedWorkflowTypes: SupportedWorkflowTypes,
            Error: error);

    private static ContextWorkflowStep BuildStep(
        int order,
        ContextWorkflowRecipeStep recipeStep,
        ContextWorkflowPlanRequest request)
    {
        var descriptor = ContextWorkflowToolCatalog.Get(recipeStep.Tool);
        return new ContextWorkflowStep(
            Order: order,
            Tool: recipeStep.Tool,
            Required: recipeStep.Required,
            Purpose: recipeStep.Purpose,
            InputHints: BuildInputHints(recipeStep.Tool, request),
            ExpectedOutput: recipeStep.ExpectedOutput,
            StopCondition: request.IncludeStopConditions ? recipeStep.StopCondition : null,
            ExecutionHint: request.IncludeExecutionHints ? recipeStep.ExecutionHint : null,
            ReadOnly: descriptor.ReadOnly,
            MutatesGraph: descriptor.MutatesGraph,
            Destructive: descriptor.Destructive,
            RequiresEmbeddings: descriptor.RequiresEmbeddings,
            RequiresExactTarget: descriptor.RequiresExactTarget);
    }

    private static IReadOnlyDictionary<string, string> BuildInputHints(string tool, ContextWorkflowPlanRequest request)
    {
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(hints, "projectContext", request.ProjectContext);

        switch (tool)
        {
            case "query_codebase":
            case "search_documentation":
            case "find_implementation_surface":
            case "plan_edit_route":
            case "hybrid_search":
                Add(hints, "query", request.Goal);
                break;
            case "analyze_feature_implementation_path":
                Add(hints, IsFeaturePath(request.Target) ? "featurePath" : "feature", request.Target ?? request.Goal);
                break;
            case "resolve_exact_symbol":
                Add(hints, "symbol", BuildSymbolHint(request));
                Add(hints, "filePath", IsLikelyFilePath(request.Target) ? request.Target : null);
                break;
            case "check_graph_freshness":
                Add(hints, "query", request.Target ?? request.Goal);
                break;
            case "find_config_definitions":
            case "find_config_usage":
                Add(hints, "canonicalKey", request.Target ?? request.Goal);
                break;
            case "replace_surface":
                Add(hints, "fromDependency", request.Target ?? request.Goal);
                Add(hints, "toDependency", "provide target replacement before executing");
                break;
            case "find_connection":
                Add(hints, "fromId", request.Target);
                Add(hints, "toId", "provide second node before executing");
                break;
            case "ingest_document":
                Add(hints, "source", request.Target);
                break;
            case "call_project_agent":
                Add(hints, "query", request.Goal);
                Add(hints, "name", request.Target);
                break;
            default:
                Add(hints, ContextWorkflowToolCatalog.Get(tool).RequiresExactTarget ? "nodeId" : "target", request.Target);
                break;
        }

        return hints;
    }

    private static IReadOnlyList<string> BuildWarnings(
        ContextWorkflowRecipe recipe,
        ContextWorkflowPlanRequest request,
        IReadOnlyList<ContextWorkflowStep> steps)
    {
        var warnings = new List<string>();
        if (recipe.UsuallyRequiresTarget && string.IsNullOrWhiteSpace(request.Target))
            warnings.Add("No target was provided. Start with the discovery steps and do not run exact-node tools until a canonical node ID is available.");

        if (steps.Any(step => step.RequiresEmbeddings))
            warnings.Add("This workflow includes embedding-dependent tools. Treat empty semantic results as an embedding availability or indexing signal, not proof of no matches.");

        if (steps.Any(step => step.MutatesGraph))
            warnings.Add("This workflow includes graph-mutating tools. Execute only after explicit user approval.");

        if (steps.Any(step => step.Destructive))
            warnings.Add("This workflow includes destructive tools. Require explicit confirmation and a project scope before execution.");

        if (steps.Count < recipe.Steps.Count(step => step.Required || request.IncludeOptionalSteps))
            warnings.Add($"The plan was truncated to {steps.Count} steps by maxSteps.");

        return warnings;
    }

    private static string InferWorkflowType(string goal, string? target)
    {
        var text = $"{goal} {target}".ToLowerInvariant();
        if (IsFeaturePath(target) || IsFeaturePath(goal))
            return "feature_implementation";
        if (ContainsAny(text, "responsibility", "slice", "namespace", "folder"))
            return "responsibility_slice_planning";
        if (ContainsAny(text, "architecture", "boundary", "layer", "cycle", "smell", "erosion", "dependency rule"))
            return "architecture_review";
        if (ContainsAny(text, "replace", "migrate", "swap", "remove dependency", "package", "framework", "library"))
            return "dependency_replacement";
        if (ContainsAny(text, "diagnostic", "error", "warning", "analyzer", "typescript", "lint", "build failure", "build error"))
            return "diagnostic_review";
        if (ContainsAny(text, "config", "environment variable", "env var", "appsettings", "options", "docker compose", "meridian.json"))
            return "configuration_review";
        if (ContainsAny(text, "stale", "knowledge", "outdated", "graph drift", "indexed facts", "weak link"))
            return "knowledge_health";
        if (ContainsAny(text, "cross project", "frontend", "backend", "api trace", "connect", "connection", "route trace"))
            return "cross_project_trace";
        if (ContainsAny(text, "similar", "duplicate", "pattern", "examples near", "semantic"))
            return "semantic_discovery";
        if (ContainsAny(text, "ingest", "remember", "external concept", "record relationship", "add document"))
            return "documentation_ingestion";
        if (ContainsAny(text, "project agent", "external agent", "register agent", "call agent", "list agents"))
            return "extension_agent_routing";
        if (ContainsAny(text, "refactor", "split", "extract", "move", "rename", "reorganize", "reduce"))
            return "refactor_planning";
        if (ContainsAny(text, "implement", "feature", "add "))
            return "feature_implementation";

        return "before_edit";
    }

    private static bool ContainsAny(string text, params string[] signals) =>
        signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool IsFeaturePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Regex.IsMatch(value, @"(^|[\\/])docs[\\/]features[\\/].+\.md$", RegexOptions.IgnoreCase);

    private static bool IsLikelyFilePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
        && value.Contains('.', StringComparison.Ordinal);

    private static string? BuildSymbolHint(ContextWorkflowPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target))
            return null;

        if (!IsLikelyFilePath(request.Target))
            return request.Target;

        var fileName = request.Target.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(fileName)
            ? request.Target
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static void Add(Dictionary<string, string> hints, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            hints[key] = value;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<ContextWorkflowRecipe> BuildRecipes() =>
    [
        Recipe(
            "before_edit",
            "Plan safe graph checks before editing a known class, method, file, endpoint, or feature.",
            "medium",
            usuallyRequiresTarget: true,
            [
                Step("resolve_exact_symbol", true, "Resolve the requested target to canonical node IDs before exact graph traversal.", "Exact or file-only node resolution.", "Stop if no target can be resolved.", "Pass symbol, filePath, and line when available."),
                Step("check_graph_freshness", true, "Check whether indexed file paths, line metadata, and timestamps are reliable.", "Freshness confidence and re-index recommendation if needed.", "Stop exact implementation work if freshness is stale."),
                Step("get_context_for_editing", true, "Inspect direct callers, callees, interfaces, file location, and size.", "Compact local edit context.", "Stop if the resolved node is missing."),
                Step("find_impact", true, "Map backward blast radius before changing behavior or signatures.", "Affected callers and transitive dependents.", "Stop broad edits if blast radius is unexpectedly high."),
                Step("find_downstream", false, "Map dependencies the target calls or relies on.", "Forward dependency surface.", "Use when internal behavior changes may affect downstream contracts."),
                Step("find_test_shield", true, "Find direct and indirect tests protecting the target.", "Direct shield, indirect shield, and unshielded path nodes.", "Add characterization tests before risky edits if shield is missing."),
                Step("find_diagnostics_for_node", false, "Check diagnostics near the target before editing.", "Nearby compiler, analyzer, TypeScript, or lint diagnostics."),
                Step("build_minimal_context", true, "Build the smallest useful source-inspection context after risk signals are known.", "Bounded context pack with tests, diagnostics, and likely files.")
            ],
            ["edit", "change", "modify", "known target"]),

        Recipe(
            "feature_implementation",
            "Plan graph-backed feature implementation discovery before editing source.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("analyze_feature_implementation_path", true, "Map the feature document or goal to likely implementation surfaces, related tests, docs, risk, and missing graph evidence.", "Implementation path analysis with confidence and risk.", "Stop if a named feature file cannot be found and no fallback goal text was provided."),
                Step("search_documentation", false, "Find indexed feature specs, ADRs, README notes, and project rules related to the goal.", "Relevant documentation evidence."),
                Step("find_implementation_surface", true, "Rank likely files, classes, and methods to edit for the feature goal.", "Candidate edit surfaces with confidence labels."),
                Step("resolve_exact_symbol", false, "Resolve the highest-confidence implementation surface to canonical node IDs.", "Exact or file-only node resolution."),
                Step("check_graph_freshness", true, "Check whether candidate implementation targets are fresh enough to trust.", "Freshness confidence and re-index recommendation if needed.", "Stop exact implementation work if graph metadata is stale."),
                Step("find_related_knowledge", false, "Find related docs, diagnostics, endpoints, and symbols through the keyword graph.", "Related knowledge with lexical confidence."),
                Step("build_minimal_context", true, "Build a bounded context pack for source inspection and implementation.", "Likely files, graph paths, tests, diagnostics, and token estimate."),
                Step("find_test_shield", true, "Find tests protecting the likely implementation surface.", "Direct and indirect test shield."),
                Step("find_diagnostics", false, "Check existing project diagnostics before planning edits.", "Current indexed diagnostics.")
            ],
            ["implement", "feature", "docs/features"]),

        Recipe(
            "refactor_planning",
            "Plan risk, coupling, and test protection before split, move, rename, or extraction work.",
            "high",
            usuallyRequiresTarget: true,
            [
                Step("resolve_exact_symbol", true, "Resolve the refactor target to a canonical node ID.", "Exact or file-only node resolution.", "Stop if target cannot be resolved."),
                Step("check_graph_freshness", true, "Verify graph freshness before trusting refactor paths.", "Freshness confidence."),
                Step("find_large_nodes", false, "Collect size evidence for classes and methods.", "Large-node candidates."),
                Step("find_god_classes", false, "Find large, heavily depended-on classes that need extra caution.", "God-class risk signals."),
                Step("suggest_extractions", false, "Find tightly connected extraction candidates.", "Extraction candidates with evidence."),
                Step("find_impact", true, "Map callers affected by signature or behavior changes.", "Backward blast radius."),
                Step("find_downstream", true, "Map dependencies used by the target.", "Forward dependency surface."),
                Step("find_test_shield", true, "Check whether the refactor path has test protection.", "Shielded and unshielded path nodes."),
                Step("find_diagnostics_for_node", false, "Check diagnostics near the target.", "Nearby diagnostics."),
                Step("build_minimal_context", true, "Build the final bounded refactor context pack.", "Context pack with likely files and tests.")
            ],
            ["refactor", "split", "extract", "move", "rename"]),

        Recipe(
            "responsibility_slice_planning",
            "Plan responsibility-based extraction slices for a large class or service.",
            "high",
            usuallyRequiresTarget: true,
            [
                Step("resolve_exact_symbol", true, "Resolve the large class or service target.", "Exact or file-only node resolution.", "Stop if target cannot be resolved."),
                Step("check_graph_freshness", true, "Verify source metadata before trusting slice recommendations.", "Freshness confidence."),
                Step("suggest_responsibility_slices", true, "Cluster methods into responsibility slices with folder, namespace, service, test, and migration guidance.", "Responsibility slice recommendations."),
                Step("find_large_nodes", false, "Collect size evidence for the target and related nodes.", "Large-node candidates."),
                Step("find_god_classes", false, "Assess whether the target is both large and highly depended on.", "God-class risk signals."),
                Step("suggest_extractions", false, "Compare slice suggestions with graph community extraction candidates.", "Extraction candidates."),
                Step("find_hotspots", false, "Use fan-in to prioritize the highest-risk slices.", "Hotspot ranking."),
                Step("find_test_shield", true, "Find tests protecting the target before extraction.", "Test shield map."),
                Step("find_coverage_gaps", false, "Identify missing test coverage near the target.", "Coverage-gap candidates."),
                Step("find_diagnostics_for_node", false, "Check diagnostics near the target.", "Nearby diagnostics."),
                Step("build_minimal_context", true, "Build a bounded context pack for the first slice.", "Context pack with likely files and tests.")
            ],
            ["responsibility", "slice", "namespace", "folder"]),

        Recipe(
            "architecture_review",
            "Plan a broad architecture, boundary, cycle, and dependency-smell review.",
            "high",
            usuallyRequiresTarget: false,
            [
                Step("get_architectural_overview", true, "Start with the project structure and major namespaces.", "Architecture overview."),
                Step("find_cycles", true, "Find namespace cycles before deeper smell-path analysis.", "Cycle findings."),
                Step("architecture_drift_history", false, "Review recent architecture erosion signals.", "Drift timeline."),
                Step("find_architecture_violations", true, "Find configured architecture rule violations.", "Violation list."),
                Step("find_smell_paths", true, "Explain architecture violations as graph paths.", "Shortest smell paths."),
                Step("find_god_classes", false, "Find large coupled classes that raise architecture risk.", "God-class candidates."),
                Step("find_hotspots", false, "Use fan-in to prioritize risky architecture nodes.", "Hotspot ranking."),
                Step("find_high_churn", false, "Prioritize issues where churn is high.", "High-churn nodes."),
                Step("find_coverage_gaps", false, "Surface untested areas around risky architecture nodes.", "Coverage-gap candidates."),
                Step("suggest_extractions", false, "Suggest extraction candidates for high-risk clusters.", "Extraction candidates.")
            ],
            ["architecture", "boundary", "cycle", "smell", "erosion"]),

        Recipe(
            "dependency_replacement",
            "Plan safe replacement of a package, API, framework, abstraction, or library.",
            "high",
            usuallyRequiresTarget: true,
            [
                Step("replace_surface", true, "Group usages into safe and risky replacement clusters.", "Replacement surface guidance.", "Stop if the source dependency is unknown."),
                Step("find_impact", true, "Map callers affected by replacement work.", "Backward blast radius."),
                Step("find_downstream", true, "Map dependencies touched by the old surface.", "Forward dependency surface."),
                Step("find_test_shield", true, "Check tests before recommending replacement order.", "Test shield map."),
                Step("find_diagnostics", false, "Check current diagnostics before starting replacement.", "Indexed diagnostics."),
                Step("find_config_usage", false, "Find config usage when the dependency is configured.", "Configuration usage."),
                Step("find_config_definitions", false, "Find config definitions when the dependency is configured.", "Configuration definitions."),
                Step("search_documentation", false, "Find docs that mention the dependency or migration rules.", "Documentation evidence."),
                Step("build_minimal_context", true, "Build a bounded replacement context pack.", "Context pack with likely files and tests.")
            ],
            ["replace", "migrate", "swap", "dependency", "package"]),

        Recipe(
            "knowledge_health",
            "Plan graph freshness, stale knowledge, and documentation reliability checks.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("find_graph_drift", true, "Check broad graph drift before trusting exact targets.", "Graph drift findings."),
                Step("check_graph_freshness", true, "Check freshness for the goal or target slice.", "Freshness confidence."),
                Step("find_stale_knowledge", true, "Find stale docs, weak mentions, and orphaned references.", "Stale-knowledge findings."),
                Step("knowledge_decay", false, "Run the graph-native stale-knowledge alias for decay-oriented workflows.", "Knowledge decay findings."),
                Step("find_related_knowledge", false, "Connect docs, diagnostics, and code through the keyword graph.", "Related knowledge."),
                Step("search_documentation", false, "Search docs for the stale or disputed concept.", "Documentation evidence.")
            ],
            ["stale", "knowledge", "drift", "outdated"]),

        Recipe(
            "diagnostic_review",
            "Plan review of build errors, analyzer warnings, TypeScript errors, lint issues, or target diagnostics.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("find_diagnostics", true, "Start with project diagnostics when no exact target is known.", "Indexed diagnostics."),
                Step("resolve_exact_symbol", false, "Resolve a target if the diagnostic mentions a known symbol or file.", "Exact or file-only node resolution."),
                Step("find_diagnostics_for_node", false, "Inspect diagnostics near the resolved target.", "Nearby diagnostics."),
                Step("find_impact", false, "Map callers if the diagnostic fix can affect behavior or signatures.", "Backward blast radius."),
                Step("find_test_shield", false, "Check tests before editing the fix surface.", "Test shield map."),
                Step("build_minimal_context", true, "Build context for the likely fix surface.", "Context pack with likely files and tests.")
            ],
            ["diagnostic", "error", "warning", "analyzer", "lint"]),

        Recipe(
            "configuration_review",
            "Plan review of config keys, environment variables, options binding, and appsettings usage.",
            "medium",
            usuallyRequiresTarget: true,
            [
                Step("find_config_definitions", true, "Find where the canonical key is defined or overridden.", "Configuration definitions.", "Stop if no canonical key can be identified."),
                Step("find_config_usage", true, "Find code that reads or binds the key.", "Configuration usage."),
                Step("find_related_knowledge", false, "Connect related docs and diagnostics through keywords.", "Related knowledge."),
                Step("find_impact", false, "Map callers if changing the setting affects behavior.", "Backward blast radius."),
                Step("find_diagnostics", false, "Check project diagnostics before changing config code.", "Indexed diagnostics."),
                Step("build_minimal_context", false, "Build context for code that uses the setting.", "Context pack.")
            ],
            ["config", "env", "options", "appsettings"]),

        Recipe(
            "cross_project_trace",
            "Plan tracing across projects, modules, frontend/backend boundaries, APIs, and routes.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("find_cross_project_dependencies", true, "Start with project boundary edges.", "Cross-project dependencies."),
                Step("find_connection", false, "Find a path between two known nodes.", "Shortest graph path."),
                Step("find_downstream", false, "Trace forward dependencies from a known node.", "Forward dependency surface."),
                Step("find_impact", false, "Trace backward callers to a known node.", "Backward blast radius."),
                Step("search_documentation", false, "Search docs for route or integration notes.", "Documentation evidence."),
                Step("find_related_knowledge", false, "Find lexically related routes, docs, diagnostics, and symbols.", "Related knowledge."),
                Step("build_minimal_context", false, "Build context when trace results will guide source inspection.", "Context pack.")
            ],
            ["cross project", "frontend", "backend", "trace", "route"]),

        Recipe(
            "semantic_discovery",
            "Plan embedding and lexical discovery for similar code, duplicate patterns, or examples.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("find_similar_nodes", false, "Find broad semantic similarity around a known node.", "Similar nodes.", "Treat empty results as embedding-dependent."),
                Step("hybrid_search", true, "Search semantically while staying near a subsystem or node.", "Hybrid semantic and graph matches.", "Treat empty results as embedding-dependent."),
                Step("find_duplicate_candidates", false, "Find likely duplicate methods or classes for refactor review.", "Duplicate candidates."),
                Step("find_related_knowledge", false, "Find lexical matches when embeddings are unavailable or weak.", "Related knowledge."),
                Step("find_test_shield", false, "Check tests before acting on duplicate or similar-code findings.", "Test shield map."),
                Step("build_minimal_context", false, "Build context for the selected candidate.", "Context pack.")
            ],
            ["similar", "duplicate", "semantic", "pattern"]),

        Recipe(
            "documentation_ingestion",
            "Plan explicit graph-memory ingestion for docs, concepts, routes, tables, topics, or service dependencies.",
            "medium",
            usuallyRequiresTarget: false,
            [
                Step("ingest_document", true, "Ingest a document only when the user explicitly wants CodeMeridian to remember it.", "Stored knowledge document.", "Require explicit user approval before graph mutation."),
                Step("link_external_concept", false, "Link external concepts such as routes, tables, topics, and services.", "External concept link.", "Require explicit user approval before graph mutation."),
                Step("ingest_relationship", false, "Record a relationship emitted by trusted automation.", "Stored graph relationship.", "Require explicit user approval before graph mutation."),
                Step("find_related_knowledge", false, "Verify whether the ingested knowledge can be discovered.", "Related knowledge."),
                Step("find_stale_knowledge", false, "Check for weak or stale references after ingestion.", "Stale-knowledge findings.")
            ],
            ["ingest", "remember", "external concept", "record"]),

        Recipe(
            "extension_agent_routing",
            "Plan listing and calling registered external project agents.",
            "low",
            usuallyRequiresTarget: false,
            [
                Step("list_project_agents", true, "List registered agents and their capabilities before routing.", "Agent list and health."),
                Step("call_project_agent", true, "Call only the relevant selected agent with the user question.", "Agent response.", "Stop if no relevant healthy agent exists."),
                Step("register_project_agent", false, "Register an agent only when the user explicitly asks to add one.", "Registered agent.", "Require explicit user intent before mutation."),
                Step("unregister_project_agent", false, "Unregister an agent only when the user explicitly asks to remove one.", "Removed agent.", "Require explicit user intent before mutation.")
            ],
            ["agent", "project agent", "external agent"])
    ];

    private static ContextWorkflowRecipe Recipe(
        string workflowType,
        string summary,
        string estimatedCost,
        bool usuallyRequiresTarget,
        IReadOnlyList<ContextWorkflowRecipeStep> steps,
        IReadOnlyList<string> matchedSignals) =>
        new(
            workflowType,
            summary,
            estimatedCost,
            usuallyRequiresTarget,
            steps,
            matchedSignals,
            [
                "State graph freshness before giving implementation advice.",
                "Separate exact graph facts from heuristic or embedding-dependent matches.",
                "List files or node IDs to inspect before editing.",
                "List tests to run or add when test evidence is weak.",
                "Do not claim source was edited by a planning workflow."
            ]);

    private static ContextWorkflowRecipeStep Step(
        string tool,
        bool required,
        string purpose,
        string expectedOutput,
        string? stopCondition = null,
        string? executionHint = null) =>
        new(tool, required, purpose, expectedOutput, stopCondition, executionHint);
}
