using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<ContextWorkflowApplicability?> ValidateConnectionBoundaryAsync(ConnectionAnalysisResult path,
        ContextWorkflowApplicability before, CancellationToken cancellationToken)
    {
        ContextWorkflowApplicability Refuse(string reason, string status = "unknown") => before with
        { Status = status, Coverage = "unknown", Reasons = [reason] };
        if (path.Nodes.Count == 0) return null;
        if (path.Nodes.Count > path.MaxHops + 1 || path.Nodes[0].Id != path.FromId || path.Nodes[^1].Id != path.ToId
            || path.Nodes.Any(node => node.ProjectContext != before.Project))
            return Refuse("The returned path does not match the verified project and query boundaries.");
        var frontend = IsFrontend(before.TargetKind);
        try
        {
            foreach (var node in path.Nodes.DistinctBy(node => node.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(node.FilePath))
                    return Refuse("A path node has no inventory source; shared-concept ownership has not been audited.");
                var context = await codeGraph.GetContextForEditingAsync(node.Id, cancellationToken);
                if (context?.Node is not { } exact || exact.Id != node.Id || exact.ProjectContext != before.Project
                    || exact.FilePath != node.FilePath || exact.Type.ToString() != node.Type)
                    return Refuse("A returned path node's identity, type, and source could not be confirmed.");
                if (frontend ? node.Type is not ("File" or "ExternalConcept")
                    : node.Type is not ("Class" or "Struct" or "Interface" or "Method" or "Delegate" or "Property"
                        or "Field" or "Event" or "Indexer" or "Operator" or "Enum" or "File" or "Module" or "Namespace"))
                    return Refuse("The path contains node types outside the query's audited relationship family.");
                var profile = await projectProfiles!.GetAsync(before.Project!, node.FilePath, cancellationToken);
                if (profile.Generation != before.ProfileGeneration || profile.Status == "stale")
                    return Refuse("Profile generation changed while validating path sources. Results were discarded.", "stale");
                var kind = profile.Target?.Kind;
                if (profile.Target?.Status != "observed" || (frontend ? !IsFrontend(kind)
                    : kind is not ("csharp" or "typescript" or "javascript")))
                    return Refuse("The path traverses a source kind outside the query's audited relationship family.");
                var applicability = ContextWorkflowToolPolicy.Evaluate("find_connection", profile);
                if (applicability.Status != "ready") return applicability;
            }
            var allowed = frontend
                ? new[] { "UsesClass", "UsesId", "DefinesSelector", "ImportsStyle", "UsesCssVariable", "DefinesCssVariable" }
                : ["Calls", "Uses", "Implements", "Inherits", "Overrides", "DependsOn"];
            if (path.Edges.Count != path.Nodes.Count - 1 || path.Edges.Any(edge => !allowed.Contains(edge.Relationship)))
                return Refuse("The path contains relationships outside the query's audited relationship family.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Refuse("Path capability evidence could not be read; results were discarded.", "failed") with
            { Limitations = [ex.GetType().Name] };
        }
        return null;
    }

    private static bool IsFrontend(string? kind) => kind is "html" or "css" or "scss";
}
