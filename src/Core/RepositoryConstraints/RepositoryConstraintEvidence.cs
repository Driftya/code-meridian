using System.Text.Json.Serialization;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

// Optional discovery publication evidence, atomically owned by its enclosing profile generation.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepositoryConstraintEvidence(
    string PolicyPath,
    string PolicyFingerprint,
    bool? CaseSensitive,
    RepositoryConstraintPolicy Policy,
    IReadOnlyList<string> ExcludedPaths)
{
    public string ContractVersion { get; init; } = "1.0";

    public string? Validate(ProjectProfileSnapshot snapshot)
    {
        if (ContractVersion != "1.0" || CaseSensitive is null || !ProjectAnalyzerFingerprint.ValidHash(PolicyFingerprint)
            || !ProjectAnalyzerFingerprint.ValidHash(snapshot.Profile.SourceFingerprint)
            || !ProjectAnalyzerFingerprint.ValidHash(snapshot.Profile.ConfigurationFingerprint))
            return "Constraint evidence requires version 1.0, explicit case semantics, and policy, source, and configuration fingerprints.";
        try
        {
            if (RepositoryConstraintPath.Normalize(PolicyPath) != PolicyPath
                || !snapshot.Files.Any(file => file.Path == PolicyPath))
                return "Constraint policy must identify an accepted inventory file.";
            if (ExcludedPaths is null || ExcludedPaths.Count > 100000
                || ExcludedPaths.Any(path => RepositoryConstraintPath.Normalize(path) != path))
                return "Constraint exclusions must contain at most 100000 canonical repository-relative paths.";
        }
        catch (ArgumentException) { return "Constraint policy or exclusion paths are invalid."; }
        if (Policy is null || Policy.Validate().Count > 0) return "Constraint policy is invalid.";
        var comparer = CaseSensitive.Value ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var paths = new HashSet<string>(comparer);
        var excluded = new HashSet<string>(ExcludedPaths, comparer);
        foreach (var file in snapshot.Files)
        {
            if (!paths.Add(file.Path)) return "Constraint inventory paths collide under the declared case semantics.";
            var path = file.Path;
            while (true)
            {
                if (excluded.Contains(path)) return "Accepted inventory paths cannot also be excluded.";
                var separator = path.LastIndexOf('/');
                if (separator < 0) break;
                path = path[..separator];
            }
        }
        return null;
    }
}
