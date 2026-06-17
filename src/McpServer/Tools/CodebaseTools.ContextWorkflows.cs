using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

public sealed partial class CodebaseTools
{
    [McpServerTool(Name = "plan_context_workflow")]
    [Description(
        "Plan the recommended sequence of CodeMeridian tools for a task. " +
        "Use this when an agent needs to know which CodeMeridian tools to call, in what order, and why. " +
        "The planner is deterministic and returns ordered steps, required/optional flags, input hints, expected outputs, stop conditions, and execution guidance.")]
    public Task<string> PlanContextWorkflowAsync(
        [Description("Plain-language task goal, e.g. 'Plan how to refactor CodebaseQueryService safely'.")]
        string goal,
        [Description("Optional target symbol, node ID, file path, feature path, config key, dependency, or agent name.")]
        string? target = null,
        [Description("Optional project name to scope the workflow.")]
        string? projectContext = null,
        [Description("Optional workflow type. Supported examples: before_edit, feature_implementation, refactor_planning, responsibility_slice_planning, architecture_review, dependency_replacement, diagnostic_review, configuration_review, semantic_discovery, knowledge_health, documentation_ingestion, extension_agent_routing.")]
        string? workflowType = null,
        [Description("Maximum number of steps to return. Default 12.")]
        int maxSteps = 12,
        [Description("Include optional recipe steps. Default true.")]
        bool includeOptionalSteps = true,
        [Description("Include stop conditions for required and risky steps. Default true.")]
        bool includeStopConditions = true,
        [Description("Include execution hints for callers that will run tools manually. Default true.")]
        bool includeExecutionHints = true,
        CancellationToken cancellationToken = default) =>
        queryService.PlanContextWorkflowAsync(
            goal,
            target,
            projectContext,
            workflowType,
            maxSteps,
            includeOptionalSteps,
            includeStopConditions,
            includeExecutionHints,
            cancellationToken);

    [McpServerTool(Name = "execute_context_workflow")]
    [Description(
        "Execute an approved read-only context workflow plan and return step results. " +
        "This tool refuses graph-mutating or destructive steps unless explicitly approved, and it stops when a required step lacks the target input it needs. " +
        "Use plan_context_workflow first when the user only asked for a plan.")]
    public Task<string> ExecuteContextWorkflowAsync(
        [Description("Plain-language task goal.")]
        string goal,
        [Description("Optional target symbol, node ID, file path, feature path, config key, dependency, or agent name.")]
        string? target = null,
        [Description("Optional project name to scope the workflow.")]
        string? projectContext = null,
        [Description("Optional workflow type to execute. Omit to infer from goal and target.")]
        string? workflowType = null,
        [Description("Maximum number of planned steps to execute. Default 8.")]
        int maxSteps = 8,
        [Description("Include optional recipe steps in execution. Default true.")]
        bool includeOptionalSteps = true,
        [Description("Allow graph-mutating workflows. Default false.")]
        bool allowGraphMutation = false,
        [Description("Optional second node ID for connection/trace workflows.")]
        string? secondaryTarget = null,
        [Description("Optional replacement dependency for dependency_replacement workflows.")]
        string? toDependency = null,
        CancellationToken cancellationToken = default) =>
        queryService.ExecuteContextWorkflowAsync(
            goal,
            target,
            projectContext,
            workflowType,
            maxSteps,
            includeOptionalSteps,
            allowGraphMutation,
            secondaryTarget,
            toDependency,
            cancellationToken);
}
