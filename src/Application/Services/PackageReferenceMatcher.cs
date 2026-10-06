using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class PackageReferenceMatcher
{
    public PackageReferenceResolution Match(PendingPackageReference pending, IReadOnlyList<PackageExportCandidate> exports)
    {
        var reference = pending.Reference;
        var candidates = exports.Where(candidate => !string.Equals(candidate.ProjectContext, pending.ProjectContext, StringComparison.OrdinalIgnoreCase)
                && candidate.Export.SymbolKey == reference.SymbolKey
                && candidate.Scope.AssemblyName == reference.AssemblyName
                && AssemblyOwner(candidate.Scope.AssemblyIdentity) == AssemblyOwner(reference.AssemblyIdentity)
                && (reference.ProducerProject is null || string.Equals(candidate.ProjectContext, reference.ProducerProject, StringComparison.OrdinalIgnoreCase))
                && (reference.PackageId is null || string.Equals(reference.PackageId, candidate.Scope.PackageId, StringComparison.OrdinalIgnoreCase))
                && (reference.RepositoryUrl is null || SameRepository(reference.RepositoryUrl, candidate.Scope.RepositoryUrl)))
            .ToArray();
        // A multi-target library is one producer, but distinct incompatible exports are not interchangeable.
        var groups = candidates.GroupBy(candidate => (candidate.ProjectContext, candidate.Export.SourceId)).ToArray();
        if (groups.Length == 0)
            return new("source_not_indexed", "No indexed producer export matches the bound assembly and symbol.");
        if (groups.Length != 1 || candidates.Any(candidate => candidate.Export.AmbiguousSource))
            return new("ambiguous", "Multiple producer definitions qualify; no source edge was created.",
                Candidates: candidates.Select(candidate => candidate.ProjectContext + ":" + candidate.Scope.ProjectPath)
                    .Distinct().Order(StringComparer.Ordinal).Take(10).ToArray());
        var verified = candidates.Where(candidate => !candidate.Scope.Dirty
                && !string.IsNullOrWhiteSpace(reference.RepositoryCommit)
                && reference.RepositoryCommit == candidate.Scope.Commit
                && SameRepository(reference.RepositoryUrl, candidate.Scope.RepositoryUrl)
                && reference.AssemblyIdentity == candidate.Scope.AssemblyIdentity
                && reference.PackageVersion == candidate.Scope.PackageVersion
                && candidate.Scope.TargetFramework == (reference.DependencyFramework ?? pending.Scope.TargetFramework))
            .ToArray();
        var selected = (verified.Length > 0 ? verified : candidates)
            .OrderBy(candidate => candidate.Scope.TargetFramework == (reference.DependencyFramework ?? pending.Scope.TargetFramework) ? 0 : 1)
            .ThenBy(candidate => candidate.Scope.PackageVersion == reference.PackageVersion ? 0 : 1)
            .ThenBy(candidate => candidate.Scope.Id, StringComparer.Ordinal).First();
        return new(verified.Length > 0 ? "verified_source" : "associated_current_source",
            verified.Length > 0 ? "Package repository revision, clean source, assembly, version and framework match."
                : "Symbol and producer match; the installed artifact is not verified against this source revision.",
            selected.Export.SourceId, selected.ProjectContext, selected.Scope.PackageVersion);
    }

    private static string AssemblyOwner(string identity) => string.Join(",", identity.Split(',')
        .Where(part => !part.TrimStart().StartsWith("Version=", StringComparison.Ordinal)).Select(part => part.Trim()));

    private static bool SameRepository(string? left, string? right) => left is not null && right is not null
        && string.Equals(PackageRepositoryIdentity.Normalize(left), PackageRepositoryIdentity.Normalize(right), StringComparison.Ordinal);
}
