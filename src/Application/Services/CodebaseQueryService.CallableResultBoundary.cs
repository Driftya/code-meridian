using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<ContextWorkflowApplicability?> ValidateCallableResultBoundaryAsync<T>(T result,
        ContextWorkflowApplicability before, CancellationToken cancellationToken)
    {
        IEnumerable<GraphNodeResult> nodes = result switch
        {
            ImpactAnalysisResult impact => impact.Findings.Select(finding => finding.Node)
                .Concat(impact.Findings.SelectMany(finding => finding.PathSegments.Select(step => step.Node))),
            MinimalContextResult context => (context.Target is { } target ? new[] { target } : [])
                .Concat(context.Callers).Concat(context.Callees).Concat(context.Interfaces)
                .Concat(context.Impact.Select(item => item.Node)).Concat(context.Downstream.Select(item => item.Node))
                .Concat(context.CoverageGaps).Concat(context.Tests.Select(test => test.TestNode))
                .Concat(context.Snippets.Select(snippet => snippet.Node)),
            _ => []
        };
        ContextWorkflowApplicability Refuse(string reason, string status = "unknown") => before with
        { Status = status, Coverage = "unknown", Reasons = [reason] };
        try
        {
            // Validate each returned representation, including snippets and intermediate impact steps.
            foreach (var node in nodes.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node.ProjectContext != before.Project || string.IsNullOrWhiteSpace(node.FilePath)
                    || node.Type is not ("Class" or "Struct" or "Interface" or "Method" or "Delegate" or "Property"
                        or "Field" or "Event" or "Indexer" or "Operator" or "Enum" or "File" or "Module" or "Namespace"))
                    return Refuse("A returned node is outside the verified callable project, source, or node family.");
                var exact = (await codeGraph.GetContextForEditingAsync(node.Id, cancellationToken))?.Node;
                if (exact is null || exact.Id != node.Id || exact.ProjectContext != before.Project
                    || exact.FilePath != node.FilePath || exact.Type.ToString() != node.Type)
                    return Refuse("A returned node's exact identity, type, and source could not be confirmed.");
                var profile = await projectProfiles!.GetAsync(before.Project!, node.FilePath, cancellationToken);
                if (profile.Generation != before.ProfileGeneration || profile.Status == "stale")
                    return Refuse("Profile generation changed while validating related sources. Results were discarded.", "stale");
                if (profile.Target is not { Status: "observed", Kind: "csharp" or "typescript" or "javascript" })
                    return Refuse("A returned node's source is outside the verified callable inventory.");
                var applicability = ContextWorkflowToolPolicy.Evaluate("build_minimal_context", profile);
                if (applicability.Status != "ready") return applicability;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Refuse("Related-source capability evidence could not be read; results were discarded.", "failed") with
            { Limitations = [ex.GetType().Name] };
        }
        return null;
    }
}
