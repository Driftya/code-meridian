namespace CodeMeridian.Application.Services.ContextWorkflows;

public sealed record ContextWorkflowPlanRequest(
    string Goal,
    string? Target = null,
    string? ProjectContext = null,
    string? WorkflowType = null,
    int MaxSteps = 12,
    bool IncludeOptionalSteps = true,
    bool IncludeStopConditions = true,
    bool IncludeExecutionHints = true);

public sealed record ContextWorkflowPlanResult(
    string Status,
    string WorkflowId,
    string WorkflowType,
    string? Project,
    string? Target,
    string Summary,
    bool RequiresApprovalBeforeExecution,
    string EstimatedCost,
    IReadOnlyList<ContextWorkflowStep> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FinalResponseGuidance,
    IReadOnlyList<string> SupportedWorkflowTypes,
    string? Error = null);

public sealed record ContextWorkflowStep(
    int Order,
    string Tool,
    bool Required,
    string Purpose,
    IReadOnlyDictionary<string, string> InputHints,
    string ExpectedOutput,
    string? StopCondition,
    string? ExecutionHint,
    bool ReadOnly,
    bool MutatesGraph,
    bool Destructive,
    bool RequiresEmbeddings,
    bool RequiresExactTarget);

public sealed record ContextWorkflowToolDescriptor(
    string Name,
    string Category,
    string Description,
    bool ReadOnly = true,
    bool MutatesGraph = false,
    bool Destructive = false,
    bool RequiresEmbeddings = false,
    bool RequiresExactTarget = false,
    bool WorksFromVagueGoal = false);

public sealed record ContextWorkflowRecipe(
    string WorkflowType,
    string Summary,
    string EstimatedCost,
    bool UsuallyRequiresTarget,
    IReadOnlyList<ContextWorkflowRecipeStep> Steps,
    IReadOnlyList<string> MatchedSignals,
    IReadOnlyList<string> FinalResponseGuidance);

public sealed record ContextWorkflowRecipeStep(
    string Tool,
    bool Required,
    string Purpose,
    string ExpectedOutput,
    string? StopCondition = null,
    string? ExecutionHint = null);

public sealed record ContextWorkflowExecutionResult(
    string Status,
    string WorkflowId,
    string WorkflowType,
    string? Project,
    string? Target,
    string Summary,
    IReadOnlyList<ContextWorkflowStepExecutionResult> Steps,
    IReadOnlyList<string> Warnings,
    string? Error = null);

public sealed record ContextWorkflowStepExecutionResult(
    int Order,
    string Tool,
    bool Required,
    string Status,
    string? Output = null,
    string? Error = null);
