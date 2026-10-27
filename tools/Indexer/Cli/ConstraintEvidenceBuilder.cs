using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class ConstraintEvidenceBuilder
{
    public static async Task<RepositoryConstraintEvidence?> BuildAsync(ResolvedIndexerSettings settings,
        ProjectProfileSnapshot snapshot, RepositoryFileInventory inventory, AnalyzedSourceSnapshot source,
        CancellationToken cancellationToken)
    {
        var path = ProjectProfilePath.Normalize(settings.ArchitecturePath ?? CodeMeridianConfigFileStore.DefaultArchitecturePath);
        if (!snapshot.Files.Any(file => file.Path == path))
        {
            if (path != CodeMeridianConfigFileStore.DefaultArchitecturePath)
                throw new IOException("The configured architecture policy is missing or outside the accepted inventory.");
            return null;
        }
        var content = await source.ReadStructuralSourceAsync(new(Path.Combine(settings.RootPath.FullName, path)), cancellationToken);
        var policy = ArchitectureRuleSetConfiguration.Parse(content).RepositoryConstraints;
        if (policy is null) return null; // Namespace-only policies keep their existing semantics.
        var sensitive = DirectoryCaseSensitivity.Read(settings.RootPath, inventory.Directories, cancellationToken);
        if (sensitive is null)
        {
            Console.Error.WriteLine("Repository constraints remain incomplete: uniform filesystem case semantics could not be established for the inventory.");
            return null;
        }
        source.BindConstraintInventory(inventory, sensitive.Value);
        return new(path, source.FileFingerprint(path), sensitive, policy, inventory.ExcludedPaths);
    }
}
