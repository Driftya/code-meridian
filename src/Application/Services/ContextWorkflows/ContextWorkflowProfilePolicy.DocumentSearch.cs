using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services.ContextWorkflows;

public static partial class ContextWorkflowProfilePolicy
{
    public static ContextWorkflowApplicability EvaluateDocumentSearch(ProjectProfileResult profile, DocumentQueryScope? scope = null)
    {
        scope ??= new(); // A target hint alone never narrows a project-wide storage query.
        ContextWorkflowApplicability Result(string status, string reason) =>
            new(status, profile.Generation, profile.Target?.Kind, scope.EvidencePath, [reason]);
        if (profile.Status == "failed") return Result("failed", "Project capability evidence could not be read.");
        if (profile.Profile is null) return Result("unknown", "Publish a complete project profile before relying on document coverage.");
        if (profile.Status != "available") return Result(profile.Status, "Discovery is not current; retained facts do not establish current coverage.");
        if (profile.Profile.DiscoveryState != "complete") return Result("unknown", "Incomplete discovery cannot establish document coverage.");
        if (scope.TargetPath is not null && profile.Target?.Path != scope.TargetPath)
            return Result("unknown", "Confirm the exact document target in the current accepted inventory.");
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
        var outcomes = profile.AnalyzerOutcomes.Where(item => item.Analyzer == "documents" && scope.Overlaps(item.Scope)).ToArray();
        if (outcomes.Length == 0) return Result("not_indexed", "Complete document indexing has not been published for this scope.");
        if (outcomes.Any(item => item.Fingerprint != analyzer.Fingerprint))
            return Result("stale", "Overlapping document outcome fingerprints do not match the published analyzer profile.");
        if (profile.Profile.SourceFingerprint is null || outcomes.Any(item => item.SourceFingerprint != profile.Profile.SourceFingerprint))
            return Result("stale", "Overlapping document outcomes are not bound to the published source content.");
        if (outcomes.Any(item => item.State == "failed"))
            return Result("failed", "Document analysis failed in an overlapping scope.");
        if (outcomes.Any(item => item.State == "partial" || item.Coverage == "partial"))
            return Result("partial", "An overlapping document scope has incomplete evidence.");
        if (outcomes.Any(item => item.State == "running"))
            return Result("not_indexed", "Document analysis is still running in an overlapping scope.");
        if (outcomes.Any(item => item.State != "succeeded" || item.Coverage != "complete" || item.Mode != "full"))
            return Result("unknown", "A delta or unaudited pass is insufficient to establish complete document coverage.");
        if (!outcomes.Any(item => scope.IsCoveredBy(item.Scope)))
            return Result("unknown", "Child/root-only outcomes cannot establish coverage for a broader document query.");
        return Result("ready", "A successful full document outcome covers the requested query; every overlapping scope has current complete evidence.");
    }
}
