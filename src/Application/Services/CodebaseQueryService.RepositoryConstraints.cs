namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<string> ExecuteRepositoryConstraintStepAsync(ContextWorkflows.ContextWorkflowStep step,
        string? projectContext, CancellationToken cancellationToken)
    {
        var result = projectProfiles is null || string.IsNullOrWhiteSpace(projectContext) ? null
            : await projectProfiles.FindRepositoryConstraintsAsync(projectContext,
                step.InputHints.TryGetValue("targetPath", out var target) ? target : null, cancellationToken);
        if (result is null) throw new InvalidOperationException("Repository constraint evidence is unavailable.");
        var evaluated = result.Evaluation is not null && result.Status is "evaluated" or "incomplete";
        RecordApplicability(step.Tool, new(evaluated ? "ok" : result.Status, result.Generation, null, result.TargetPath,
            ["Inspect per-rule applicability and coverage independently of searchable text and semantic analysis."])
            { Project = result.Project, Coverage = result.Coverage });
        return result.ToMarkdown();
    }
}
