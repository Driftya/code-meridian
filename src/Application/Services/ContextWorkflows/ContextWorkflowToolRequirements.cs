namespace CodeMeridian.Application.Services.ContextWorkflows;

internal static class ContextWorkflowToolRequirements
{
    internal static readonly string[] CodeKinds = ["csharp", "typescript", "javascript"];

    public static ContextWorkflowToolDescriptor Apply(ContextWorkflowToolDescriptor tool, ContextWorkflowToolOptions? options = null)
    {
        options ??= new();
        var capabilities = tool.Name switch
        {
            "search_documentation" => new[] { "document_search" },
            "find_repository_constraint_violations" => ["repository_inventory", "repository_constraints"],
            "find_config_definitions" => ["configuration_definitions"],
            "find_config_usage" => ["configuration_definitions", "configuration_usage"],
            "find_test_shield" or "find_coverage_gaps" => ["callable_definitions", "callable_dependencies", "test_to_code"],
            "find_cross_project_dependencies" => ["callable_definitions", "callable_dependencies", "cross_project_dependencies"],
            "find_architecture_violations" or "find_smell_paths"
                => ["namespace_classification", "callable_dependencies", "architecture_rules"],
            "find_cycles" => ["namespace_classification", "callable_dependencies"],
            "architecture_drift_history" => ["namespace_classification", "callable_dependencies", "architecture_history"],
            "get_architecture_weather_report" => ["callable_definitions", "namespace_classification", "callable_dependencies", "architecture_rules", "test_to_code"],
            "get_architectural_overview" => ["callable_definitions", "namespace_classification"],
            "find_recently_changed" or "find_high_churn" => ["callable_definitions", "change_history"],
            "find_large_nodes" => ["callable_definitions", "source_locations"],
            "get_pagerank" or "get_betweenness" or "find_natural_modules" or "find_bridges"
                => ["callable_definitions", "callable_dependencies", "graph_projection"],
            "suggest_extractions" => ["callable_definitions", "callable_dependencies", "graph_projection", "test_to_code"],
            "plan_edit_route" or "replace_surface" or "find_implementation_patterns" => ["callable_definitions", "callable_dependencies", "test_to_code"],
            "analyze_changed_subgraph"
                => ["callable_definitions", "callable_dependencies", "test_to_code", "document_search", "namespace_classification", "architecture_rules", "change_history"],
            "suggest_responsibility_slices"
                => ["callable_definitions", "callable_dependencies", "test_to_code", "namespace_classification"],
            "analyze_feature_implementation_path"
                => ["callable_definitions", "callable_dependencies", "test_to_code", "document_search"],
            "find_diagnostics" or "find_diagnostics_for_node" => ["diagnostics"],
            "trace_endpoint" => ["endpoint_tracing", "callable_dependencies"],
            "find_frontend_cascade_conflicts" => ["frontend_cascade"],
            "find_stale_knowledge" or "knowledge_decay" or "find_related_knowledge" => ["knowledge_links"],
            "resolve_exact_symbol" or "find_implementation_surface"
                => ["callable_definitions"],
            _ => ["callable_definitions", "callable_dependencies"]
        };
        if (tool.Name is "analyze_feature_implementation_path" or "analyze_changed_subgraph" or "suggest_responsibility_slices")
            capabilities = capabilities.Where(capability =>
                (options.IncludeTests || capability != "test_to_code")
                && (options.IncludeDocs || capability != "document_search")
                && (options.IncludeNamespacePlan || tool.Name != "suggest_responsibility_slices" || capability != "namespace_classification")).ToArray();
        var kinds = tool.Name switch
        {
            "search_documentation" => new[] { "markdown", "text" },
            "find_repository_constraint_violations" => ["csharp", "typescript", "javascript", "markdown", "text", "configuration", "sql", "html", "css", "scss", "powershell", "asset", "other", "typescript_declaration"],
            "find_config_definitions" => ["configuration"],
            "resolve_exact_symbol" => [.. CodeKinds, "powershell"],
            "find_frontend_cascade_conflicts" => ["html", "css", "scss", "typescript", "javascript"],
            "find_related_knowledge" => ["csharp", "typescript", "javascript", "markdown", "text", "configuration", "sql", "html", "css", "scss"],
            _ => CodeKinds
        };
        var exempt = tool.MutatesGraph || tool.Category is "Ingestion" or "ClientExtensions" || tool.Name is "get_project_profile"
            or "check_graph_freshness" or "find_graph_drift" or "find_tool_dependency_impact";
        return tool with
        {
            RequiredCapabilities = exempt ? [] : tool.RequiresEmbeddings ? capabilities.Append("semantic_embeddings").ToArray() : capabilities,
            SupportedFileKinds = exempt ? [] : kinds,
            ReadinessExempt = exempt,
            AllowsTargetScope = tool.Name is "resolve_exact_symbol" or "search_documentation" or "find_repository_constraint_violations",
            CapabilityAlternatives = tool.Name == "find_connection"
                ? [new(CodeKinds, capabilities), new(["html", "css", "scss"], ["frontend_relationships"])]
                : tool.Name == "resolve_exact_symbol"
                    ? [new(CodeKinds, capabilities), new(["powershell"], ["powershell_definitions"])] : []
        };
    }
}
