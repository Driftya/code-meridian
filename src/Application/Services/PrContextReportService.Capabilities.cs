using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class PrContextReportService
{
    public async Task<PrContextReport> BuildAsync(PrContextReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectProfiles is null) return await BuildCoreAsync(request, cancellationToken);
        var descriptor = new ContextWorkflowToolDescriptor("pr_context_report", "Reports", "Pull request context")
        {
            SupportedFileKinds = ContextWorkflowToolRequirements.CodeKinds,
            RequiredCapabilities = ["callable_definitions", "callable_dependencies", "test_to_code", "change_history",
                .. request.IncludeDocs ? new[] { "document_search" } : Array.Empty<string>()]
        };
        var before = await EvaluateAsync();
        if (before.Status != "ready") return Block(before);
        PrContextReport report;
        try { report = await BuildCoreAsync(request, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Block(before with { Status = "failed", Coverage = "unknown", Reasons = ["Report execution failed; no successful result is available."], Limitations = [ex.GetType().Name] });
        }
        var after = await EvaluateAsync();
        if (after.Status != "ready" || after.ProfileGeneration != before.ProfileGeneration)
            return Block(after with { Status = "stale", Coverage = "unknown", Reasons = ["Profile generation or required report coverage changed during analysis. Results were discarded; retry."] });
        return report with { Applicability = after with { Status = "ok" } };

        PrContextReport Block(ContextWorkflowApplicability applicability) => new(request.ProjectContext, request.BaseRef,
            request.HeadRef, [], [], [], [], [], [], [applicability.ToMarkdown(descriptor.Name)]) { Applicability = applicability };

        async Task<ContextWorkflowApplicability> EvaluateAsync()
        {
            try
            {
                var profile = request.ProjectContext is null
                    ? new ProjectProfileResult("unknown", "unknown", null, null, null, null, null, [])
                    : await projectProfiles.GetAsync(request.ProjectContext, cancellationToken: cancellationToken);
                return ContextWorkflowToolPolicy.Evaluate(descriptor, profile);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new("failed", null, null, ".", ["Report capability evidence could not be read."])
                { Project = request.ProjectContext, Limitations = [ex.GetType().Name] };
            }
        }
    }
}
