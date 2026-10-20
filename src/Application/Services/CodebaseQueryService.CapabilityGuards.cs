using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private readonly AsyncLocal<CapabilityExecutionScope?> capabilityExecution = new();

    private async Task<ContextWorkflowApplicability?> EvaluateToolAsync(string tool, string? project, string? target,
        CancellationToken cancellationToken)
    {
        try { return await EvaluateToolCoreAsync(tool, project, target, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new("failed", null, null, null, ["Capability evidence could not be read; analysis was not authorized."])
            { Project = project, Limitations = [ex.GetType().Name], NextActions = ["Retry profile discovery before analysis."] };
        }
    }

    private async Task<ContextWorkflowApplicability?> EvaluateToolCoreAsync(string tool, string? project, string? target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectProfiles is null) return null; // Explicit legacy construction preserves advisory/manual access.
        var descriptor = ContextWorkflowToolCatalog.Get(tool);
        if (descriptor.ReadinessExempt) return null;
        var canonical = target?.Contains("::", StringComparison.Ordinal) == true;
        var nodeProject = canonical ? target![..target!.IndexOf("::", StringComparison.Ordinal)] : null;
        project ??= nodeProject;
        var profile = await GetWorkflowProfileAsync(project, canonical ? null : target, cancellationToken);
        if (canonical && profile is { Status: "available", Profile: not null })
        {
            if (project != nodeProject)
                return new("unknown", profile.Generation, null, null, ["The canonical target belongs to a different project; supply its actual project context."]) { Project = project };
            var context = await codeGraph.GetContextForEditingAsync(target!, cancellationToken);
            if (context?.Node is not { FilePath: { } path } node || node.ProjectContext != project)
                return new("unknown", profile.Generation, null, null, ["The exact target and its inventory path could not be confirmed."]) { Project = project };
            profile = await projectProfiles.GetAsync(project!, path, cancellationToken);
        }
        var applicability = ContextWorkflowToolPolicy.Evaluate(tool, profile!);
        if (applicability.Status == "ready" && descriptor.RequiresEmbeddings && !await embeddingProvider.IsAvailableAsync(cancellationToken))
            return applicability with { Status = "unknown", Coverage = "unknown", Reasons = ["The required embedding provider is unavailable."] };
        return applicability;
    }

    private async Task<string> ExecuteCapabilityQueryAsync(string tool, string? project, string? target,
        Func<Task<string>> query, CancellationToken cancellationToken)
    {
        var before = await EvaluateToolAsync(tool, project, target, cancellationToken);
        if (before is null) return await query();
        if (before.Status != "ready") { RecordApplicability(tool, before); return before.ToMarkdown(tool); }
        string output;
        try { output = await query(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = before with { Status = "failed", Coverage = "unknown", Reasons = ["Analysis execution failed; no successful result is available."], Limitations = [ex.GetType().Name] };
            RecordApplicability(tool, failed);
            return failed.ToMarkdown(tool);
        }
        var after = await RevalidateToolAsync(tool, before, target, cancellationToken);
        if (after.Status == "ready") after = after with { Status = "ok" };
        RecordApplicability(tool, after);
        return after.Status == "ok" ? after.ToMarkdown(tool) + "\n\n" + output : after.ToMarkdown(tool);
    }

    private async Task<T> ExecuteCapabilityQueryAsync<T>(string tool, string? project, string? target,
        Func<Task<T>> query, Func<ContextWorkflowApplicability, T> blocked, Func<T, ContextWorkflowApplicability, T> annotate,
        CancellationToken cancellationToken, string? secondaryTarget = null)
    {
        var before = await EvaluateToolAsync(tool, project, target, cancellationToken);
        if (before is null) return await query();
        T Block(ContextWorkflowApplicability applicability) { RecordApplicability(tool, applicability); return blocked(applicability); }
        if (before.Status != "ready") return Block(before);
        var secondary = secondaryTarget is null ? null : await EvaluateToolAsync(tool, project, secondaryTarget, cancellationToken);
        if (secondary is { Status: not "ready" }) return Block(secondary);
        if (secondary is not null && before.Project != secondary.Project)
            return Block(before with { Status = "unknown", Coverage = "unknown", Reasons = ["Cross-project path coverage has not been verified for both query boundaries."] });
        T result;
        try { result = await query(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Block(before with { Status = "failed", Coverage = "unknown", Reasons = ["Analysis execution failed; no successful result is available."], Limitations = [ex.GetType().Name] });
        }
        var after = await RevalidateToolAsync(tool, before, target, cancellationToken);
        if (after.Status != "ready") return Block(after);
        if (secondary is not null)
        {
            var secondAfter = await RevalidateToolAsync(tool, secondary, secondaryTarget, cancellationToken);
            if (secondAfter.Status != "ready") return Block(secondAfter);
        }
        if (result is ConnectionAnalysisResult connection && connection.Nodes.Any(node => node.ProjectContext != before.Project))
            return Block(before with { Status = "unknown", Coverage = "unknown", Reasons = ["The path traverses projects whose coverage has not been verified together."] });
        if (result is ImpactAnalysisResult impact && impact.Findings.Any(finding => finding.Node.ProjectContext != before.Project))
            return Block(before with { Status = "unknown", Coverage = "unknown", Reasons = ["The impact crosses projects whose caller coverage has not been verified together."] });
        after = after with { Status = "ok" };
        RecordApplicability(tool, after);
        return annotate(result, after);
    }

    private async Task<ContextWorkflowApplicability> RevalidateToolAsync(string tool, ContextWorkflowApplicability before,
        string? target, CancellationToken cancellationToken)
    {
        var after = await EvaluateToolAsync(tool, before.Project, target, cancellationToken);
        if (after?.Status != "ready" || after.ProfileGeneration != before.ProfileGeneration)
            return (after ?? before) with { Status = "stale", Coverage = "unknown",
                Reasons = ["Profile generation, target, or required coverage changed during analysis. Results were discarded; replan and retry."] };
        return after;
    }

    private void RecordApplicability(string tool, ContextWorkflowApplicability applicability)
    {
        if (capabilityExecution.Value is { } scope && scope.Tool == tool) scope.Applicability = applicability;
    }

    private sealed class CapabilityExecutionScope(string tool)
    {
        public string Tool { get; } = tool;
        public ContextWorkflowApplicability? Applicability { get; set; }
    }
}
