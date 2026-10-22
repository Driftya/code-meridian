using CodeMeridian.Application.Projects;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public static class ContextWorkflowProfilePolicy
{
    public static ContextWorkflowApplicability EvaluateDocumentSearch(ProjectProfileResult profile)
    {
        ContextWorkflowApplicability Result(string status, string reason, string? scope = null) =>
            new(status, profile.Generation, profile.Target?.Kind, scope ?? profile.Target?.Path, [reason]);
        if (profile.Status == "failed") return Result("failed", "Project capability evidence could not be read.");
        if (profile.Profile is null) return Result("unknown", "Publish a complete project profile before relying on document coverage.");
        if (profile.Status != "available") return Result(profile.Status, "Discovery is not current; retained facts do not establish current coverage.");
        if (profile.Profile.DiscoveryState != "complete") return Result("unknown", "Incomplete discovery cannot establish document coverage.");
        if (profile.Target is { Status: not "observed" }) return Result("unknown", "The requested target is not observed in the current accepted inventory.");
        if (profile.Target?.Kind is { } kind && kind is not ("markdown" or "text"))
            return Result("not_applicable", "Document search does not analyze this target kind.");
        if (!profile.Profile.FileKinds.Any(kind => kind.Kind is "markdown" or "text"))
            return Result("not_applicable", "The accepted inventory contains no document text files.");
        var analyzer = profile.Profile.Analyzers.SingleOrDefault(a => a.Id == "documents");
        if (analyzer is null) return Result("unknown", "Document analyzer support is unknown.");
        if (!analyzer.Supported || !analyzer.SupportedCapabilities.Contains("document_search"))
            return Result("unsupported", "Document search capability is not supported by this analyzer profile.");
        if (analyzer.ConfigurationState != "enabled") return Result("disabled", "Document indexing is disabled.");
        var outcome = profile.AnalyzerOutcomes.Where(outcome => outcome.Analyzer == "documents"
                && (outcome.Scope == "." || profile.Target?.Path is { } path
                    && path.StartsWith(outcome.Scope + "/", StringComparison.Ordinal)))
            .OrderByDescending(outcome => outcome.Scope == "." ? 0 : outcome.Scope.Length).FirstOrDefault();
        if (outcome is null || outcome.State == "running") return Result("not_indexed", "Complete document indexing has not been published for this scope.");
        if (profile.AnalyzerOutcomes.Any(item => item.Analyzer == "documents" && item.Fingerprint != analyzer.Fingerprint))
            return Result("stale", "Document outcome fingerprints do not match the published analyzer profile.", outcome.Scope);
        if (outcome.State is "failed" or "partial") return Result(outcome.State, "Document analysis did not complete this scope.", outcome.Scope);
        if (profile.Profile.SourceFingerprint is null
            || profile.AnalyzerOutcomes.Any(item => item.Analyzer == "documents" && item.SourceFingerprint != profile.Profile.SourceFingerprint))
            return Result("stale", "Document outcomes are not bound to the published source content.", outcome.Scope);
        if (outcome.Coverage != "complete" || outcome.Mode != "full")
            return Result("unknown", "A completed delta or unaudited pass is insufficient to establish complete document coverage.", outcome.Scope);
        if (outcome.Scope != ".")
            return Result("unknown", "Document search currently queries the project; root-only outcomes cannot establish coverage for that query.", outcome.Scope);
        if (profile.AnalyzerOutcomes.Any(o => o.Analyzer == "documents" && (o.State != "succeeded" || o.Coverage != "complete" || o.Mode != "full")))
            return Result("partial", "Another document scope has incomplete evidence; a project-wide query cannot claim complete coverage.");
        return Result("ready", "A successful full document pass covers the requested scope.", outcome.Scope);
    }

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
            var applicability = ContextWorkflowToolPolicy.Evaluate("search_documentation", profile);
            var steps = new[]
            {
                DocumentStep(1, "get_project_profile", "Check uploaded discovery and target evidence.", request,
                    new("ready", profile.Generation, profile.Target?.Kind, profile.Target?.Path, [])),
                DocumentStep(2, "search_documentation", "Read indexed document context before editing or reviewing.", request, applicability)
            };
            var truncated = request.MaxSteps < steps.Length;
            return plan with
            {
                WorkflowId = "document_review", WorkflowType = "document_review",
                Summary = "Review document context using inventory and searchable text evidence.",
                Steps = steps, RequiresApprovalBeforeExecution = false, EstimatedCost = "low",
                Status = applicability.Status == "ready" && !truncated ? "valid" : "blocked",
                Applicability = applicability,
                Error = truncated ? "maxSteps must include both required document steps." : applicability.Status == "ready" ? null : applicability.Reasons[0],
                Warnings = profile.Warnings.Concat(truncated ? ["Required document steps were retained rather than silently truncated."] : applicability.Status == "ready" ? [] : applicability.Reasons).ToArray(),
                FinalResponseGuidance = ["Describe indexed text coverage. Placement, Markdown reference validity, and narrative consistency are not established by document search."]
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
        ContextWorkflowPlanRequest request, ContextWorkflowApplicability applicability) => new(
        order, tool, true, purpose, DocumentHints(tool, request),
        "Uploaded discovery facts or indexed document text.", "Stop unless document coverage is ready.",
        "This read-only workflow does not ingest documents or validate references.", true, false, false, false, false)
        { Applicability = applicability };

    private static IReadOnlyDictionary<string, string> DocumentHints(string tool, ContextWorkflowPlanRequest request)
    {
        var hints = new Dictionary<string, string> { ["projectContext"] = request.ProjectContext ?? "supply explicit project" };
        if (tool == "get_project_profile")
        {
            if (!string.IsNullOrWhiteSpace(request.Target)) hints["targetPath"] = request.Target;
        }
        else hints["query"] = string.IsNullOrWhiteSpace(request.Target) ? request.Goal : request.Target;
        return hints;
    }
}
