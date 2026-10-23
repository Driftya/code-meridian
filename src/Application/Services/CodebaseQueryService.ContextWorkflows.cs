using System.Text.Json;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private static readonly ContextWorkflowPlanner WorkflowPlanner = new();
    private static readonly JsonSerializerOptions WorkflowJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string> PlanContextWorkflowAsync(
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
            includeExecutionHints), await GetWorkflowProfileAsync(projectContext, target, cancellationToken));

        return JsonSerializer.Serialize(result, WorkflowJsonOptions);
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
            IncludeExecutionHints: true), await GetWorkflowProfileAsync(projectContext, target, cancellationToken));

        if (plan.Status != "valid")
        {
            return SerializeExecution(new ContextWorkflowExecutionResult(
                Status: plan.Status == "blocked" ? "blocked" : "validation_error",
                WorkflowId: plan.WorkflowId,
                WorkflowType: plan.WorkflowType,
                Project: plan.Project,
                Target: plan.Target,
                Summary: plan.Summary,
                Steps: [],
                Warnings: plan.Warnings,
                Error: plan.Error) { Applicability = plan.Applicability });
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
        var executionTarget = target;
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projectProfiles is not null)
            {
                var current = await GetWorkflowProfileAsync(projectContext, target, cancellationToken);
                if (current is not null && (step.Tool == "search_documentation" && plan.WorkflowType != "document_review"
                    || plan.WorkflowType == "feature_implementation" && current.Target?.Kind is "markdown" or "text"))
                    current = current with { Target = null };
                var documentScope = step.Tool == "search_documentation" && step.InputHints.TryGetValue("targetPath", out var documentTarget)
                    ? new DocumentQueryScope(targetPath: documentTarget) : null;
                var readiness = ContextWorkflowToolPolicy.Evaluate(step.Tool, current!, new(DocumentScope: documentScope));
                if (current?.Generation != plan.Applicability?.ProfileGeneration || readiness.Status != "ready")
                {
                    var applicability = readiness with { Status = current?.Generation != plan.Applicability?.ProfileGeneration ? "stale" : readiness.Status };
                    results.Add(new(step.Order, step.Tool, step.Required, applicability.Status, Error: string.Join(" ", applicability.Reasons)) { Applicability = applicability });
                    if (step.Required || applicability.Status == "stale") break;
                    continue;
                }
                if (step.RequiresExactTarget && executionTarget is not null && !executionTarget.Contains("::", StringComparison.Ordinal))
                {
                    executionTarget = await ResolveCanonicalNodeIdAsync(executionTarget, projectContext, cancellationToken);
                    if (!executionTarget.Contains("::", StringComparison.Ordinal))
                    {
                        results.Add(new(step.Order, step.Tool, step.Required, "missing_input", Error: "Resolve an unambiguous canonical node ID before executing this exact-target step."));
                        break;
                    }
                }
            }
            var result = await ExecuteWorkflowStepAsync(step, goal, step.Tool is "resolve_exact_symbol" or "search_documentation" ? target : executionTarget,
                projectContext, secondaryTarget, toDependency, cancellationToken);
            results.Add(result);

            if (step.Required && result.Status != "completed")
                break;
        }

        if (projectProfiles is not null && results.Count > 0
            && results.Count(result => result.Required && result.Status == "completed") == plan.Steps.Count(step => step.Required))
        {
            var current = await GetWorkflowProfileAsync(projectContext, target, cancellationToken);
            var replanned = WorkflowPlanner.Plan(new(goal, target, projectContext, workflowType, maxSteps, includeOptionalSteps), current);
            if (current?.Generation != plan.Applicability?.ProfileGeneration || replanned.Status != "valid")
            {
                results = results.Select(result => result with { Output = null }).ToList();
                var stale = (replanned.Applicability ?? plan.Applicability!) with { Status = "stale", Coverage = "unknown",
                    Reasons = ["Profile generation or required coverage changed during execution; outputs were discarded."] };
                results.Add(new(results.Count + 1, "get_project_profile", true, "stale", Error: stale.Reasons[0]) { Applicability = stale });
            }
        }

        if (results.Any(result => result.Status == "stale")) results = results.Select(result => result with { Output = null }).ToList();

        var status = results.Any(result => result.Status is "failed")
            ? "failed"
            : results.Any(result => result.Status is "missing_input" or "unsupported_tool")
                ? "stopped"
                : results.Any(result => result.Status == "stale" || result.Required && result.Status is not "completed")
                    || results.Count(result => result.Required && result.Status == "completed") != plan.Steps.Count(step => step.Required) ? "blocked"
                : projectProfiles is not null && plan.Applicability?.Status == "unknown" ? "completed_with_unknown_coverage" : "completed";

        return SerializeExecution(new ContextWorkflowExecutionResult(
            Status: status,
            WorkflowId: plan.WorkflowId,
            WorkflowType: plan.WorkflowType,
            Project: plan.Project,
            Target: plan.Target,
            Summary: plan.Summary,
            Steps: results,
            Warnings: plan.Warnings) { Applicability = results.LastOrDefault(item => item.Applicability is not null)?.Applicability ?? plan.Applicability });
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

        var previousCapabilityScope = capabilityExecution.Value;
        var capabilityScope = new CapabilityExecutionScope(step.Tool);
        capabilityExecution.Value = capabilityScope;
        try
        {
            var output = step.Tool switch
            {
                "get_project_profile" => (await GetWorkflowProfileAsync(projectContext, target, cancellationToken))?.ToMarkdown()
                    ?? "Project profile is unknown; supply an explicit project context.",
                "query_codebase" => await QueryStructureAsync(goal, projectContext, cancellationToken),
                "get_architectural_overview" => await GetOverviewAsync(projectContext, cancellationToken),
                "search_documentation" => await SearchDocumentationAsync(target ?? goal, projectContext, cancellationToken,
                    targetPath: step.InputHints.TryGetValue("targetPath", out var documentTarget) ? documentTarget : null),
                "find_config_definitions" => await FindConfigDefinitionsAsync(target ?? goal, projectContext, cancellationToken),
                "find_config_usage" => await FindConfigUsageAsync(target ?? goal, projectContext, cancellationToken),
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
                "find_implementation_patterns" => await FindImplementationPatternsAsync(goal, projectContext, cancellationToken: cancellationToken),
                "find_duplicate_candidates" => await FindDuplicateCandidatesAsync(projectContext, cancellationToken: cancellationToken),
                "find_diagnostics" => await FindDiagnosticsAsync(projectContext, cancellationToken: cancellationToken),
                "find_diagnostics_for_node" => await WithTargetAsync(step, target, nodeId => FindDiagnosticsForNodeAsync(nodeId, cancellationToken)),
                "find_implementation_surface" => await FindImplementationSurfaceAsync(goal, projectContext: projectContext, cancellationToken: cancellationToken),
                "analyze_feature_implementation_path" => await AnalyzeFeatureImplementationPathAsync(target ?? goal, projectContext, cancellationToken: cancellationToken),
                "plan_edit_route" => await PlanEditRouteAsync(goal, projectContext: projectContext, cancellationToken: cancellationToken),
                "replace_surface" => await WithReplacementTargetsAsync(step, target, toDependency, (from, to) => ReplaceSurfaceAsync(from, to, projectContext, cancellationToken: cancellationToken)),
                "suggest_extractions" => await SuggestExtractionsAsync(projectContext, cancellationToken: cancellationToken),
                "suggest_responsibility_slices" => await WithTargetAsync(step, target, nodeId => SuggestResponsibilitySlicesAsync(nodeId, projectContext, cancellationToken: cancellationToken)),
                "resolve_exact_symbol" => await ResolveWorkflowTargetAsync(step, target, projectContext, cancellationToken),
                "check_graph_freshness" => await CheckGraphFreshnessAsync(target ?? goal, projectContext, cancellationToken: cancellationToken),
                "find_graph_drift" => await FindGraphDriftAsync(projectContext, cancellationToken: cancellationToken),
                "find_stale_knowledge" => await FindStaleKnowledgeAsync(projectContext, cancellationToken: cancellationToken),
                "knowledge_decay" => await FindStaleKnowledgeAsync(projectContext, cancellationToken: cancellationToken),
                "find_related_knowledge" => await MissingTargetAsync(step),
                "rebuild_keyword_graph" or "classify_keywords" or "get_client_extension_contract" or "list_client_extension_examples" or "get_client_extension_example" => await UnsupportedAsync(step),
                _ => await UnsupportedAsync(step)
            };

            if (capabilityScope.Applicability is { } applicability)
                return new(step.Order, step.Tool, step.Required, applicability.Status == "ok" ? "completed" : applicability.Status,
                    Output: applicability.Status == "ok" ? output : null,
                    Error: applicability.Status == "ok" ? null : output) { Applicability = applicability };

            if (IsSemanticallyFailedWorkflowOutput(step.Tool, output))
            {
                return new ContextWorkflowStepExecutionResult(
                    step.Order,
                    step.Tool,
                    step.Required,
                    "failed",
                    Error: output);
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ContextWorkflowStepExecutionResult(step.Order, step.Tool, step.Required, "failed", Error: ex.Message);
        }
        finally { capabilityExecution.Value = previousCapabilityScope; }
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

    private async Task<string> ResolveWorkflowTargetAsync(
        ContextWorkflowStep step,
        string? target,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new MissingWorkflowInputException($"{step.Tool} requires a target or canonical node ID.");

        if (LooksLikeCanonicalNodeId(target))
            return $"Target is already canonical: `{target}`.";

        var profile = await GetWorkflowProfileAsync(projectContext, target, cancellationToken);
        return await ResolveExactSymbolAsync(profile?.Target?.Path is { } path ? Path.GetFileNameWithoutExtension(path) : target,
            filePath: profile?.Target?.Path, projectContext: projectContext, cancellationToken: cancellationToken);
    }

    private static bool IsSemanticallyFailedWorkflowOutput(string tool, string output) =>
        tool == "resolve_exact_symbol"
        && output.StartsWith("No exact symbol candidates found", StringComparison.OrdinalIgnoreCase);

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
