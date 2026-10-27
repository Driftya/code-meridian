using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public static partial class ContextWorkflowProfilePolicy
{
    public static ContextWorkflowPlanResult Apply(ContextWorkflowPlanRequest request, ContextWorkflowPlanResult plan, ProjectProfileResult profile)
    {
        if (plan.Status != "valid") return plan;
        var documentTarget = profile.Target is { Status: "observed", Kind: "markdown" or "text" };
        var documentRepository = string.IsNullOrWhiteSpace(request.Target) && profile.Profile is { } inventory
            && inventory.FileKinds.Any(k => k.Kind is "markdown" or "text")
            && inventory.FileKinds.All(k => k.Kind is "markdown" or "text" or "configuration" or "asset");
        if (plan.WorkflowType == "document_review" || (documentTarget || documentRepository)
            && plan.WorkflowType is "before_edit" or "refactor_planning")
        {
            var documentScope = new DocumentQueryScope(targetPath: string.IsNullOrWhiteSpace(request.Target) ? null : profile.Target?.Path);
            var applicability = ContextWorkflowToolPolicy.Evaluate("search_documentation", profile, new(DocumentScope: documentScope));
            var constraintApplicability = ContextWorkflowToolPolicy.Evaluate("find_repository_constraint_violations", profile);
            var steps = new[]
            {
                DocumentStep(1, "get_project_profile", "Check uploaded discovery and target evidence.", request,
                    new("ready", profile.Generation, profile.Target?.Kind, profile.Target?.Path, []), documentScope),
                DocumentStep(2, "search_documentation", "Read indexed document context before editing or reviewing.", request, applicability, documentScope)
            };
            var constraintStep = DocumentStep(3, "find_repository_constraint_violations", "Evaluate placement and structural links independently of searchable text.", request, constraintApplicability, documentScope)
                with { Required = false };
            var includeConstraints = request.IncludeOptionalSteps != false && constraintApplicability.Status == "ready" && request.MaxSteps > steps.Length;
            var truncated = request.MaxSteps < steps.Length;
            return plan with
            {
                WorkflowId = "document_review", WorkflowType = "document_review",
                Summary = "Review document context using inventory and searchable text evidence.",
                Steps = includeConstraints ? [.. steps, constraintStep] : steps,
                OmittedSteps = includeConstraints ? [] : [constraintStep],
                RequiresApprovalBeforeExecution = false, EstimatedCost = "low",
                Status = applicability.Status == "ready" && !truncated ? "valid" : "blocked",
                Applicability = applicability,
                Error = truncated ? "maxSteps must include both required document steps." : applicability.Status == "ready" ? null : applicability.Reasons[0],
                Warnings = profile.Warnings.Concat(truncated ? ["Required document steps were retained rather than silently truncated."] : applicability.Status == "ready" ? [] : applicability.Reasons)
                    .Concat(includeConstraints ? [] : ["Optional repository constraint check omitted: " + (request.IncludeOptionalSteps == false ? "optional steps disabled."
                        : constraintApplicability.Status == "ready" ? "maxSteps budget." : string.Join(" ", constraintApplicability.Reasons))]).ToArray(),
                FinalResponseGuidance = ["Describe searchable text and per-rule constraint coverage separately. Incomplete references and unverified fragments cannot establish clean validation; narrative consistency is outside these checks."]
            };
        }
        var evaluated = plan.Steps.Select(step => step with { Applicability = ContextWorkflowToolPolicy.Evaluate(step.Tool,
            (step.Tool == "search_documentation" || plan.WorkflowType == "feature_implementation" && documentTarget)
                ? profile with { Target = null } : profile) }).ToArray();
        var omitted = evaluated.Where(step => !step.Required && step.Applicability!.Status != "ready").ToArray();
        var retained = evaluated.Except(omitted).ToArray();
        var blocker = retained.FirstOrDefault(step => step.Required && step.Applicability!.Status != "ready");
        var applicabilityResult = blocker?.Applicability ?? new("ready", profile.Generation, profile.Target?.Kind, ".",
            ["Every required step has published evidence for its declared semantics."])
            { Project = profile.Project, Coverage = "complete" };
        return plan with
        {
            Status = blocker is null ? "valid" : "blocked", Error = blocker?.Applicability!.Reasons.FirstOrDefault(),
            Applicability = applicabilityResult, OmittedSteps = omitted,
            Steps = retained.Select((step, index) => step with { Order = index + 1 }).ToArray(),
            Warnings = plan.Warnings.Concat(profile.Warnings).Concat(blocker is null ? [] : ["Required capability evidence is unavailable; retained steps are advisory and execution is blocked."]).Concat(omitted.Select(step =>
                $"Omitted optional {step.Tool}: {step.Applicability!.Status}. {string.Join(" ", step.Applicability.Reasons)}")).ToArray()
        };
    }

    private static ContextWorkflowStep DocumentStep(int order, string tool, string purpose,
        ContextWorkflowPlanRequest request, ContextWorkflowApplicability applicability, DocumentQueryScope scope) => new(
        order, tool, true, purpose, DocumentHints(tool, request, scope),
        "Uploaded discovery facts or indexed document text.", "Stop unless document coverage is ready.",
        "This read-only workflow does not ingest documents or validate references.", true, false, false, false, false)
        { Applicability = applicability };

    private static IReadOnlyDictionary<string, string> DocumentHints(string tool, ContextWorkflowPlanRequest request, DocumentQueryScope scope)
    {
        var hints = new Dictionary<string, string> { ["projectContext"] = request.ProjectContext ?? "supply explicit project" };
        if (tool is "get_project_profile" or "find_repository_constraint_violations")
        {
            if (scope.TargetPath is not null) hints["targetPath"] = scope.TargetPath;
        }
        else
        {
            hints["query"] = string.IsNullOrWhiteSpace(request.Target) ? request.Goal : request.Target;
            if (scope.TargetPath is not null) hints["targetPath"] = scope.TargetPath;
        }
        return hints;
    }
}
