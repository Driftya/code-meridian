using System.Text.Json;
using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private static readonly ContextWorkflowPlanner WorkflowPlanner = new();
    private static readonly JsonSerializerOptions WorkflowJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<string> PlanContextWorkflowAsync(
        string goal,
        string? target = null,
        string? projectContext = null,
        string? workflowType = null,
        int maxSteps = 12,
        bool? includeOptionalSteps = null,
        bool includeStopConditions = true,
        bool includeExecutionHints = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = WorkflowPlanner.Plan(new ContextWorkflowPlanRequest(
            goal,
            target,
            projectContext,
            workflowType,
            maxSteps,
            includeOptionalSteps,
            includeStopConditions,
            includeExecutionHints));

        return Task.FromResult(JsonSerializer.Serialize(result, WorkflowJsonOptions));
    }

    public async Task<string> ExecuteContextWorkflowAsync(
        string goal,
        string? target = null,
        string? projectContext = null,
        string? workflowType = null,
        int maxSteps = 8,
        bool? includeOptionalSteps = null,
        bool allowGraphMutation = false,
        string? secondaryTarget = null,
        string? toDependency = null,
        CancellationToken cancellationToken = default)
    {
        var plan = WorkflowPlanner.Plan(new ContextWorkflowPlanRequest(
            goal,
            target,
            projectContext,
            workflowType,
            maxSteps,
            includeOptionalSteps,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        if (plan.Status != "valid")
        {
            return SerializeExecution(new ContextWorkflowExecutionResult(
                Status: "validation_error",
                WorkflowId: plan.WorkflowId,
                WorkflowType: plan.WorkflowType,
                Project: plan.Project,
                Target: plan.Target,
                Summary: plan.Summary,
                Steps: [],
                Warnings: plan.Warnings,
                Error: plan.Error));
        }

        var mutatingSteps = plan.Steps.Where(step => step.MutatesGraph || step.Destructive).ToArray();
        if (mutatingSteps.Length > 0 && !allowGraphMutation)
        {
            return SerializeExecution(new ContextWorkflowExecutionResult(
                Status: "approval_required",
                WorkflowId: plan.WorkflowId,
                WorkflowType: plan.WorkflowType,
                Project: plan.Project,
                Target: plan.Target,
                Summary: plan.Summary,
                Steps: mutatingSteps.Select(step => new ContextWorkflowStepExecutionResult(
                    step.Order,
                    step.Tool,
                    step.Required,
                    "not_executed",
                    Error: "Graph mutation requires explicit approval.")).ToArray(),
                Warnings: plan.Warnings.Append("Execution refused because the plan includes graph-mutating tools.").ToArray(),
                Error: "Graph mutation requires explicit approval."));
        }

        var results = new List<ContextWorkflowStepExecutionResult>();
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteWorkflowStepAsync(step, goal, target, projectContext, secondaryTarget, toDependency, cancellationToken);
            results.Add(result);

            if (step.Required && result.Status is "missing_input" or "unsupported_tool" or "failed")
                break;
        }

        var status = results.Any(result => result.Status is "failed")
            ? "failed"
            : results.Any(result => result.Status is "missing_input" or "unsupported_tool")
                ? "stopped"
                : "completed";

        return SerializeExecution(new ContextWorkflowExecutionResult(
            Status: status,
            WorkflowId: plan.WorkflowId,
            WorkflowType: plan.WorkflowType,
            Project: plan.Project,
            Target: plan.Target,
            Summary: plan.Summary,
            Steps: results,
            Warnings: plan.Warnings));
    }

    private async Task<ContextWorkflowStepExecutionResult> ExecuteWorkflowStepAsync(
        ContextWorkflowStep step,
        string goal,
        string? target,
        string? projectContext,
        string? secondaryTarget,
        string? toDependency,
        CancellationToken cancellationToken)
    {
        if (step.MutatesGraph || step.Destructive)
        {
            return new ContextWorkflowStepExecutionResult(
                step.Order,
                step.Tool,
                step.Required,
                "unsupported_tool",
                Error: "execute_context_workflow does not execute graph-mutating tools.");
        }

        try
        {
            var output = step.Tool switch
            {
                "query_codebase" => await QueryStructureAsync(goal, projectContext, cancellationToken),
                "get_architectural_overview" => await GetOverviewAsync(projectContext, cancellationToken),
                "search_documentation" => await SearchDocumentationAsync(target ?? goal, projectContext, cancellationToken),
                "find_impact" => await WithTargetAsync(step, target, nodeId => FindImpactAsync(nodeId, cancellationToken: cancellationToken)),
                "find_hotspots" => await FindHotspotsAsync(projectContext, cancellationToken),
                "find_connection" => await WithTwoTargetsAsync(step, target, secondaryTarget, (fromId, toId) => FindConnectionAsync(fromId, toId, cancellationToken: cancellationToken)),
                "find_unreferenced" => await FindUnreferencedAsync(projectContext, cancellationToken),
                "find_cross_project_dependencies" => await FindCrossProjectDependenciesAsync(projectContext, cancellationToken),
                "find_coverage_gaps" => await FindCoverageGapsAsync(projectContext, cancellationToken: cancellationToken),
                "find_test_shield" => await WithTargetAsync(step, target, nodeId => FindTestShieldAsync(nodeId, projectContext, cancellationToken: cancellationToken)),
                "find_recently_changed" => await FindRecentlyChangedAsync(projectContext, cancellationToken: cancellationToken),
                "find_large_nodes" => await FindLargeNodesAsync(projectContext, cancellationToken: cancellationToken),
                "get_context_for_editing" => await WithTargetAsync(step, target, nodeId => GetContextForEditingAsync(nodeId, cancellationToken: cancellationToken)),
                "build_minimal_context" => await WithTargetAsync(step, target, nodeId => BuildMinimalContextAsync(nodeId, goal, cancellationToken: cancellationToken)),
                "find_god_classes" => await FindGodClassesAsync(projectContext, cancellationToken),
                "find_downstream" => await WithTargetAsync(step, target, nodeId => FindDownstreamAsync(nodeId, cancellationToken: cancellationToken)),
                "find_cycles" => await FindCyclesAsync(projectContext, cancellationToken),
                "architecture_drift_history" => await FindArchitectureErosionTimelineAsync(projectContext, cancellationToken: cancellationToken),
                "find_architecture_violations" => await FindArchitectureViolationsAsync(projectContext, cancellationToken),
                "find_smell_paths" => await FindSmellPathsAsync(projectContext, cancellationToken: cancellationToken),
                "find_high_churn" => await FindHighChurnAsync(projectContext, cancellationToken: cancellationToken),
                "find_similar_nodes" => await WithTargetAsync(step, target, nodeId => FindSimilarToNodeAsync(nodeId, projectContext, cancellationToken)),
                "hybrid_search" => await FindHybridSearchAsync(goal, target, projectContext: projectContext, cancellationToken: cancellationToken),
                "find_duplicate_candidates" => await FindDuplicateCandidatesAsync(projectContext, cancellationToken: cancellationToken),
                "find_diagnostics" => await FindDiagnosticsAsync(projectContext, cancellationToken: cancellationToken),
                "find_diagnostics_for_node" => await WithTargetAsync(step, target, nodeId => FindDiagnosticsForNodeAsync(nodeId, cancellationToken)),
                "find_implementation_surface" => await FindImplementationSurfaceAsync(goal, projectContext: projectContext, cancellationToken: cancellationToken),
                "analyze_feature_implementation_path" => await AnalyzeFeatureImplementationPathAsync(target ?? goal, projectContext, cancellationToken: cancellationToken),
                "plan_edit_route" => await PlanEditRouteAsync(goal, projectContext: projectContext, cancellationToken: cancellationToken),
                "replace_surface" => await WithReplacementTargetsAsync(step, target, toDependency, (from, to) => ReplaceSurfaceAsync(from, to, projectContext, cancellationToken: cancellationToken)),
                "suggest_extractions" => await SuggestExtractionsAsync(projectContext, cancellationToken: cancellationToken),
                "suggest_responsibility_slices" => await WithTargetAsync(step, target, nodeId => SuggestResponsibilitySlicesAsync(nodeId, projectContext, cancellationToken: cancellationToken)),
                "resolve_exact_symbol" => await WithTargetAsync(step, target, symbol => ResolveExactSymbolAsync(symbol, projectContext: projectContext, cancellationToken: cancellationToken)),
                "check_graph_freshness" => await CheckGraphFreshnessAsync(target ?? goal, projectContext, cancellationToken: cancellationToken),
                "find_graph_drift" => await FindGraphDriftAsync(projectContext, cancellationToken: cancellationToken),
                "find_stale_knowledge" => await FindStaleKnowledgeAsync(projectContext, cancellationToken: cancellationToken),
                "knowledge_decay" => await FindStaleKnowledgeAsync(projectContext, cancellationToken: cancellationToken),
                "find_related_knowledge" => await MissingTargetAsync(step),
                "rebuild_keyword_graph" or "classify_keywords" or "list_project_agents" or "call_project_agent" => await UnsupportedAsync(step),
                _ => await UnsupportedAsync(step)
            };

            return new ContextWorkflowStepExecutionResult(step.Order, step.Tool, step.Required, "completed", output);
        }
        catch (MissingWorkflowInputException ex)
        {
            return new ContextWorkflowStepExecutionResult(step.Order, step.Tool, step.Required, "missing_input", Error: ex.Message);
        }
        catch (UnsupportedWorkflowToolException ex)
        {
            return new ContextWorkflowStepExecutionResult(step.Order, step.Tool, step.Required, "unsupported_tool", Error: ex.Message);
        }
        catch (Exception ex)
        {
            return new ContextWorkflowStepExecutionResult(step.Order, step.Tool, step.Required, "failed", Error: ex.Message);
        }
    }

    private static async Task<string> WithTargetAsync(
        ContextWorkflowStep step,
        string? target,
        Func<string, Task<string>> execute)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new MissingWorkflowInputException($"{step.Tool} requires a target or canonical node ID.");

        return await execute(target);
    }

    private static async Task<string> WithTwoTargetsAsync(
        ContextWorkflowStep step,
        string? firstTarget,
        string? secondTarget,
        Func<string, string, Task<string>> execute)
    {
        if (string.IsNullOrWhiteSpace(firstTarget) || string.IsNullOrWhiteSpace(secondTarget))
            throw new MissingWorkflowInputException($"{step.Tool} requires target and secondaryTarget.");

        return await execute(firstTarget, secondTarget);
    }

    private static async Task<string> WithReplacementTargetsAsync(
        ContextWorkflowStep step,
        string? fromDependency,
        string? toDependency,
        Func<string, string, Task<string>> execute)
    {
        if (string.IsNullOrWhiteSpace(fromDependency) || string.IsNullOrWhiteSpace(toDependency))
            throw new MissingWorkflowInputException($"{step.Tool} requires target/fromDependency and toDependency.");

        return await execute(fromDependency, toDependency);
    }

    private static Task<string> MissingTargetAsync(ContextWorkflowStep step) =>
        throw new MissingWorkflowInputException($"{step.Tool} requires a canonical source node ID from a previous step.");

    private static Task<string> UnsupportedAsync(ContextWorkflowStep step) =>
        throw new UnsupportedWorkflowToolException($"{step.Tool} is not executable through execute_context_workflow in this slice. Call the tool directly after reviewing the plan.");

    private static string SerializeExecution(ContextWorkflowExecutionResult result) =>
        JsonSerializer.Serialize(result, WorkflowJsonOptions);

    private sealed class MissingWorkflowInputException(string message) : Exception(message);

    private sealed class UnsupportedWorkflowToolException(string message) : Exception(message);
}
