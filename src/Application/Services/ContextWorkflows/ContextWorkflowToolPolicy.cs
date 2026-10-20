using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public static class ContextWorkflowToolPolicy
{
    public static ContextWorkflowApplicability Evaluate(string tool, ProjectProfileResult profile)
    {
        var descriptor = ContextWorkflowToolCatalog.Get(tool);
        ContextWorkflowApplicability Result(string status, string reason, string? scope = null,
            IReadOnlyList<string>? limitations = null) => new(status, profile.Generation, profile.Target?.Kind,
                scope ?? (descriptor.AllowsTargetScope ? profile.Target?.Path : "."), new[] { reason }.Concat(profile.Warnings).Take(8).ToArray())
            {
                Project = profile.Project,
                Coverage = status == "ready" ? "complete" : "unknown",
                Limitations = (limitations ?? []).Take(8).ToArray(),
                NextActions = status == "ready" ? [] : ["Inspect get_project_profile and the requested scope. Refresh the required indexing evidence explicitly before retrying."]
            };
        if (descriptor.ReadinessExempt) return Result("ready", "This discovery or metadata tool does not require semantic readiness.") with { Coverage = "not_required" };
        if (profile.Status == "failed") return Result("failed", "Project capability evidence could not be read.");
        if (tool == "search_documentation")
        {
            var document = ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile);
            return document with { Project = profile.Project, Coverage = document.Status == "ready" ? "complete" : "unknown",
                NextActions = document.Status == "ready" ? [] : ["Inspect the profile and explicitly refresh complete document indexing before retrying."] };
        }
        if (profile.Profile is null) return Result("unknown", "No complete project discovery profile is available.");
        if (profile.Status != "available") return Result(profile.Status == "stale" ? "stale" : "unknown", "Discovery is not current.");
        if (profile.Profile.DiscoveryState != "complete") return Result("unknown", "Incomplete discovery cannot establish target or language absence.");
        if (profile.Target is { Status: not "observed" }) return Result("unknown", "Confirm the target in the current accepted inventory before analysis.");
        var alternatives = descriptor.CapabilityAlternatives.Count == 0
            ? new[] { new ContextWorkflowCapabilityAlternative(descriptor.SupportedFileKinds, descriptor.RequiredCapabilities) }
            : descriptor.CapabilityAlternatives;
        if (profile.Target?.Kind == "powershell") return Result("unsupported", "PowerShell analysis is not implemented.");
        if (profile.Target?.Kind is { } targetKind && !alternatives.Any(item => item.FileKinds.Contains(targetKind)))
            return Result("not_applicable", "This tool's audited node and relationship semantics do not apply to the observed target kind.");
        var kinds = descriptor.AllowsTargetScope && profile.Target?.Kind is { } kind ? [kind]
            : profile.Profile.FileKinds.Where(item => item.Count > 0).Select(item => item.Kind).ToArray();
        var requirements = alternatives.SelectMany(alternative => kinds.Where(alternative.FileKinds.Contains)
            .SelectMany(kind => alternative.Capabilities.Select(capability => (Analyzer: AnalyzerFor(tool, kind, capability), Capability: capability))))
            .Distinct().OrderBy(item => item.Analyzer, StringComparer.Ordinal).ThenBy(item => item.Capability, StringComparer.Ordinal).ToArray();
        if (requirements.Length == 0) return Result("not_applicable", "The complete inventory contains no file kinds supported by this tool.");
        var evaluations = requirements.GroupBy(item => item.Analyzer).Select(group => EvaluateAnalyzer(group.Key, group.Select(item => item.Capability).ToArray())).ToArray();
        var blocked = evaluations.Where(item => item.Status != "ready").OrderBy(item => StatusPriority(item.Status)).FirstOrDefault();
        if (blocked is not null) return blocked with
        {
            Reasons = evaluations.Where(item => item.Status != "ready").SelectMany(item => item.Reasons).Distinct().Take(8).ToArray(),
            Limitations = evaluations.SelectMany(item => item.Limitations).Distinct().Take(8).ToArray()
        };
        return Result("ready", "Published full coverage satisfies this tool's declared requirements.",
            descriptor.AllowsTargetScope ? profile.Target?.Path : ".", evaluations.SelectMany(item => item.Limitations).Distinct().Take(8).ToArray());

        ContextWorkflowApplicability EvaluateAnalyzer(string id, string[] capabilities)
        {
            var analyzer = profile.Profile.Analyzers.SingleOrDefault(item => item.Id == id);
            if (analyzer is null) return Result("unknown", $"Analyzer support is unknown for {id}.");
            if (!analyzer.Supported) return Result("unsupported", $"Analyzer {id} is unsupported.");
            if (analyzer.ConfigurationState != "enabled") return Result("disabled", $"Analyzer {id} is disabled.");
            var missing = capabilities.Where(capability => !analyzer.SupportedCapabilities.Contains(capability)).ToArray();
            if (missing.Length > 0) return Result("unknown", $"Analyzer {id} has not published support for: {string.Join(", ", missing)}.", limitations: analyzer.Limitations);
            var outcomes = profile.AnalyzerOutcomes.Where(item => item.Analyzer == id).ToArray();
            var scoped = descriptor.AllowsTargetScope && profile.Target?.Path is { };
            if (scoped) outcomes = outcomes.Where(item => Covers(item.Scope, profile.Target!.Path)).ToArray();
            if (outcomes.Length == 0) return Result("not_indexed", $"No outcomes have been published for analyzer {id} in this scope.");
            if (outcomes.Any(item => item.Fingerprint != analyzer.Fingerprint))
                return Result("stale", $"Analyzer {id} outcomes do not match the published fingerprint.");
            var failed = outcomes.FirstOrDefault(item => item.State == "failed");
            if (failed is not null) return Result("failed", $"Analyzer {id} failed in scope {failed.Scope}.", failed.Scope, failed.Limitations);
            var partial = outcomes.FirstOrDefault(item => item.State == "partial" || item.Coverage == "partial");
            if (partial is not null) return Result("partial", $"Analyzer {id} has incomplete evidence in scope {partial.Scope}.", partial.Scope, partial.Limitations);
            if (outcomes.Any(item => item.State == "running")) return Result("not_indexed", $"Analyzer {id} is still running.");
            if (outcomes.Any(item => item.State != "succeeded" || item.Mode != "full" || item.Coverage != "complete"))
                return Result("unknown", $"A delta or unaudited outcome for {id} does not establish complete coverage.");
            if (!scoped && !outcomes.Any(item => item.Scope == "."))
                return Result("unknown", $"Root-only outcomes for {id} cannot authorize this project-wide query.");
            var evidence = outcomes.OrderByDescending(item => item.Scope.Length).First();
            return Result("ready", $"Analyzer {id} has complete published coverage.", evidence.Scope,
                analyzer.Limitations.Concat(outcomes.SelectMany(item => item.Limitations)).Distinct().Take(8).ToArray());
        }
    }

    internal static bool Covers(string scope, string path) => scope == "." || path.StartsWith(scope + "/", StringComparison.Ordinal);

    private static string AnalyzerFor(string tool, string kind, string capability) => capability is "configuration_definitions" or "architecture_rules" ? "configuration"
        : capability == "document_search" ? "documents"
        : capability == "knowledge_links" ? "keywords"
        : tool == "find_frontend_cascade_conflicts" ? "frontend" : kind switch
        {
            "csharp" => "csharp", "typescript" or "javascript" => "typescript", "configuration" => "configuration",
            "markdown" or "text" => "documents", "html" or "css" or "scss" => "frontend", _ => kind
        };

    private static int StatusPriority(string status) => status switch
    {
        "failed" => 0, "stale" => 1, "partial" => 2, "unsupported" => 3, "disabled" => 4, "not_indexed" => 5, _ => 6
    };
}
