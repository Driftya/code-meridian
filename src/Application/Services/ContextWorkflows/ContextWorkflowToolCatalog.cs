namespace CodeMeridian.Application.Services.ContextWorkflows;

public static class ContextWorkflowToolCatalog
{
    private static readonly ContextWorkflowToolDescriptor[] ToolDefinitions =
    [
        Tool("query_codebase", "QueryAndExploration", "Natural-language structural search.", worksFromVagueGoal: true),
        Tool("get_architectural_overview", "QueryAndExploration", "High-level project structure map.", worksFromVagueGoal: true),
        Tool("search_documentation", "QueryAndExploration", "Search indexed docs, ADRs, README files, and comments.", worksFromVagueGoal: true),
        Tool("find_tool_dependency_impact", "QueryAndExploration", "Show which CodeMeridian tools, reports, evaluators, docs, and test suites depend on a tool or shared contract.", worksFromVagueGoal: true),
        Tool("rebuild_keyword_graph", "QueryAndExploration", "Rebuild derived keyword nodes and relationships.", readOnly: false, mutatesGraph: true),
        Tool("classify_keywords", "QueryAndExploration", "Classify derived keywords for better lexical matching.", readOnly: false, mutatesGraph: true),
        Tool("find_related_knowledge", "QueryAndExploration", "Find lexically related code, docs, and diagnostics.", requiresExactTarget: true),

        Tool("find_config_definitions", "ConfigurationGraph", "Find where a canonical configuration key is defined.", worksFromVagueGoal: true),
        Tool("find_config_usage", "ConfigurationGraph", "Find code that reads or binds a configuration key.", worksFromVagueGoal: true),

        Tool("find_impact", "GraphAnalytics", "Backward blast-radius analysis.", requiresExactTarget: true),
        Tool("find_hotspots", "GraphAnalytics", "Rank high fan-in nodes."),
        Tool("find_connection", "GraphAnalytics", "Find a graph path between two nodes.", requiresExactTarget: true),
        Tool("trace_endpoint", "GraphAnalytics", "Trace an API endpoint to indexed database and messaging paths.", worksFromVagueGoal: true),
        Tool("find_unreferenced", "GraphAnalytics", "Find dead-code candidates."),
        Tool("find_cross_project_dependencies", "GraphAnalytics", "Find dependencies crossing project boundaries."),
        Tool("find_coverage_gaps", "GraphAnalytics", "Find production nodes not called by tests."),
        Tool("find_test_shield", "GraphAnalytics", "Find direct and indirect tests protecting a node.", requiresExactTarget: true),
        Tool("find_recently_changed", "GraphAnalytics", "Find nodes changed in a time window."),
        Tool("find_large_nodes", "GraphAnalytics", "Find oversized classes and methods."),
        Tool("get_context_for_editing", "GraphAnalytics", "Return direct callers, callees, interfaces, and location.", requiresExactTarget: true),
        Tool("build_minimal_context", "GraphAnalytics", "Build a bounded context pack for a target.", requiresExactTarget: true),
        Tool("find_god_classes", "GraphAnalytics", "Find large and heavily depended-on classes."),
        Tool("find_downstream", "GraphAnalytics", "Forward dependency and call traversal.", requiresExactTarget: true),
        Tool("find_cycles", "GraphAnalytics", "Find namespace-level circular dependencies."),
        Tool("architecture_drift_history", "GraphAnalytics", "Summarize recent architecture erosion signals."),
        Tool("find_architecture_violations", "GraphAnalytics", "Find configured architecture boundary violations."),
        Tool("find_smell_paths", "GraphAnalytics", "Find graph paths that violate architecture rules."),
        Tool("find_high_churn", "GraphAnalytics", "Find nodes with high indexed change count."),

        Tool("find_similar_nodes", "SemanticAndHybridSearch", "Find semantically similar nodes.", requiresEmbeddings: true, requiresExactTarget: true),
        Tool("hybrid_search", "SemanticAndHybridSearch", "Semantic search constrained by graph neighborhood.", requiresEmbeddings: true, worksFromVagueGoal: true),
        Tool("find_duplicate_candidates", "SemanticAndHybridSearch", "Find semantically similar duplicate-code candidates.", requiresEmbeddings: true),

        Tool("find_diagnostics", "Diagnostics", "Find indexed compiler, analyzer, TypeScript, and lint diagnostics."),
        Tool("find_diagnostics_for_node", "Diagnostics", "Find diagnostics near a specific node.", requiresExactTarget: true),

        Tool("find_implementation_surface", "ImplementationPlanning", "Rank likely implementation files and symbols.", worksFromVagueGoal: true),
        Tool("analyze_feature_implementation_path", "ImplementationPlanning", "Map a feature request or feature doc to implementation evidence.", worksFromVagueGoal: true),
        Tool("plan_edit_route", "ImplementationPlanning", "Plan an ordered edit route across contracts, app, infrastructure, and tests.", worksFromVagueGoal: true),
        Tool("replace_surface", "ImplementationPlanning", "Group dependency replacement work into safe and risky clusters.", worksFromVagueGoal: true),
        Tool("suggest_extractions", "ImplementationPlanning", "Suggest tightly connected extraction candidates."),
        Tool("suggest_responsibility_slices", "ImplementationPlanning", "Suggest responsibility-based extraction slices for a large class.", requiresExactTarget: true),
        Tool("resolve_exact_symbol", "ImplementationPlanning", "Resolve a symbol, file, and line hint to canonical node IDs.", worksFromVagueGoal: true),

        Tool("check_graph_freshness", "FreshnessAndKnowledge", "Report indexed metadata freshness and trust confidence.", worksFromVagueGoal: true),
        Tool("find_graph_drift", "FreshnessAndKnowledge", "Detect stale graph data before exact implementation work."),
        Tool("find_stale_knowledge", "FreshnessAndKnowledge", "Detect stale docs, weak mentions, and orphaned graph knowledge."),
        Tool("knowledge_decay", "FreshnessAndKnowledge", "Alias for stale-knowledge review."),

        Tool("ingest_code_node", "Ingestion", "Manually add or update a code node.", readOnly: false, mutatesGraph: true),
        Tool("ingest_relationship", "Ingestion", "Manually add or update a relationship.", readOnly: false, mutatesGraph: true),
        Tool("ingest_document", "Ingestion", "Ingest a document for future search.", readOnly: false, mutatesGraph: true),
        Tool("link_external_concept", "Ingestion", "Create an external concept and link it to code.", readOnly: false, mutatesGraph: true),
        Tool("clear_project_knowledge", "Ingestion", "Clear all knowledge for a project.", readOnly: false, mutatesGraph: true, destructive: true),
        Tool("clear_code_graph", "Ingestion", "Clear all indexed code graph nodes.", readOnly: false, mutatesGraph: true, destructive: true),

        Tool("register_project_agent", "ExtensionAgents", "Register an external project agent.", readOnly: false, mutatesGraph: true),
        Tool("unregister_project_agent", "ExtensionAgents", "Remove an external project agent.", readOnly: false, mutatesGraph: true),
        Tool("list_project_agents", "ExtensionAgents", "List registered project agents."),
        Tool("call_project_agent", "ExtensionAgents", "Call a registered project agent.")
    ];

    public static IReadOnlyDictionary<string, ContextWorkflowToolDescriptor> Tools { get; } =
        ToolDefinitions.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    public static IReadOnlyList<ContextWorkflowToolDescriptor> All => ToolDefinitions;

    public static bool Contains(string toolName) => Tools.ContainsKey(toolName);

    public static ContextWorkflowToolDescriptor Get(string toolName) =>
        Tools.TryGetValue(toolName, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Workflow recipe references unknown CodeMeridian tool '{toolName}'.");

    public static IReadOnlyList<string> MissingRecipeTools(IEnumerable<ContextWorkflowRecipe> recipes) =>
        recipes
            .SelectMany(recipe => recipe.Steps.Select(step => step.Tool))
            .Where(tool => !Tools.ContainsKey(tool))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static ContextWorkflowToolDescriptor Tool(
        string name,
        string category,
        string description,
        bool readOnly = true,
        bool mutatesGraph = false,
        bool destructive = false,
        bool requiresEmbeddings = false,
        bool requiresExactTarget = false,
        bool worksFromVagueGoal = false) =>
        new(name, category, description, readOnly, mutatesGraph, destructive, requiresEmbeddings, requiresExactTarget, worksFromVagueGoal);
}
