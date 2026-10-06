using System.Runtime.CompilerServices;
using CodeMeridian.Core.CodeGraph;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class PackageReferenceIndexer
{
    private static readonly object RegistrationLock = new();

    public static async Task<PackageReferenceSnapshot> BuildAsync(string rootPath, string projectContext,
        List<IngestNodeRequest> nodes, PackageIndexingOptions options, CancellationToken cancellationToken,
        List<IngestEdgeRequest>? edges = null)
    {
        lock (RegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
        }
        return await BuildWithWorkspaceAsync(rootPath, projectContext, nodes, options, cancellationToken, edges);
    }

    // Keep MSBuild types out of the registration method: their assemblies must load after Locator registration.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<PackageReferenceSnapshot> BuildWithWorkspaceAsync(string rootPath, string projectContext,
        List<IngestNodeRequest> nodes, PackageIndexingOptions options, CancellationToken cancellationToken,
        List<IngestEdgeRequest>? edges)
    {
        var scopes = new List<PackageBuildScope>();
        var exports = new List<PackageSymbolExport>();
        var references = new List<PackageSymbolReference>();
        var diagnostics = new List<string>();
        var evaluatedNodes = new List<IngestNodeRequest>();
        var evaluatedEdges = new List<IngestEdgeRequest>();
        var ownedFiles = nodes.Where(node => node.Type == "File").Select(node => node.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projects = Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Split('/').Any(part => part is "obj" or "bin" or ".meridian" or "node_modules"))
            .Order(StringComparer.Ordinal).ToArray();
        foreach (var path in projects)
        {
            var projectMetadata = await PackageProjectMetadata.ReadAsync(path, cancellationToken);
            foreach (var framework in projectMetadata.Frameworks)
            {
                var metadata = await PackageProjectMetadata.ReadAsync(path, cancellationToken, framework);
                var assets = PackageAssetCatalog.Read(path, framework);
                using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
                {
                    ["TargetFramework"] = framework, ["Configuration"] = "Debug",
                    ["BuildProjectReferences"] = "false", ["RestoreDuringBuild"] = "false"
                });
                workspace.LoadMetadataForReferencedProjects = false;
                var project = await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken);
                var compilation = await project.GetCompilationAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Project compilation is unavailable.");
                var projectPath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
                var scopeId = PackageSymbolIdentity.Id(projectPath, framework, "Debug");
                var failed = workspace.Diagnostics.Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    || compilation.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                if (failed) diagnostics.Add(projectPath + ": project evaluation was incomplete; source provenance is unverified.");
                var scope = new PackageBuildScope(scopeId, projectPath, framework, "Debug", compilation.Assembly.Name,
                    compilation.Assembly.Identity.ToString(), metadata.PackageId, metadata.PackageVersion,
                    metadata.RepositoryUrl, metadata.Commit, metadata.Dirty || failed, "msbuild.workspace");
                scopes.Add(scope);
                var sourceProjects = new Dictionary<string, PackageAsset>(StringComparer.Ordinal);
                foreach (var referencedProject in workspace.CurrentSolution.Projects.Where(candidate => candidate.Id != project.Id && candidate.FilePath is not null))
                {
                    if (!Path.GetRelativePath(rootPath, referencedProject.FilePath!).Replace('\\', '/').StartsWith("../", StringComparison.Ordinal)) continue;
                    var referencedCompilation = await referencedProject.GetCompilationAsync(cancellationToken);
                    var referencedMetadata = await PackageProjectMetadata.ReadAsync(referencedProject.FilePath!, cancellationToken);
                    if (referencedCompilation is null || referencedMetadata.Frameworks.Count != 1) continue;
                    var identity = referencedCompilation.Assembly.Identity.ToString();
                    var value = new PackageAsset(referencedMetadata.PackageId, referencedMetadata.PackageVersion,
                        referencedMetadata.RepositoryUrl, referencedMetadata.Dirty ? null : referencedMetadata.Commit,
                        Path.GetFileName(referencedProject.FilePath!), referencedMetadata.Frameworks[0]);
                    sourceProjects[identity] = value;
                }
                var scopedNodes = new List<IngestNodeRequest>();
                var scopedEdges = new List<IngestEdgeRequest>();
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var file = Path.GetRelativePath(rootPath, tree.FilePath).Replace('\\', '/');
                    if (!ownedFiles.Contains(file)) continue;
                    new CSharpAstWalker(file, projectContext, scopedNodes, scopedEdges, compilation.GetSemanticModel(tree)).Visit(tree.GetRoot(cancellationToken));
                }
                var extractor = new PackageReferenceExtractor(rootPath, scope, compilation, assets, scopedNodes, options, sourceProjects);
                foreach (var tree in compilation.SyntaxTrees) extractor.Extract(tree, cancellationToken);
                evaluatedNodes.AddRange(scopedNodes);
                evaluatedEdges.AddRange(scopedEdges);
                exports.AddRange(extractor.Exports);
                references.AddRange(extractor.References);
            }
        }
        // Existing source IDs may aggregate different declarations. Do not choose one of those definitions.
        var collisions = exports.Where(export => !export.SymbolKey.Contains("#ctor", StringComparison.Ordinal))
            .GroupBy(export => (export.ScopeId, export.SourceId))
            .Where(group => group.Select(export => export.SymbolKey).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        var scopePaths = scopes.ToDictionary(scope => scope.Id, scope => scope.ProjectPath);
        var mergedProjects = exports.GroupBy(export => export.SourceId).Where(group => group.Select(export => scopePaths[export.ScopeId]).Distinct().Count() > 1
            && group.Select(export => export.FilePath).Distinct().Count() > 1).Select(group => group.Key).ToHashSet();
        exports = exports.Select(export => export with { AmbiguousSource = collisions.Contains((export.ScopeId, export.SourceId)) || mergedProjects.Contains(export.SourceId) }).ToList();
        // Include declarations activated by evaluated framework constants that the syntax fallback did not see.
        var knownLocations = nodes.Select(NodeLocation).ToHashSet(StringComparer.Ordinal);
        var added = evaluatedNodes.Where(node => knownLocations.Add(NodeLocation(node))).ToArray();
        nodes.AddRange(added);
        var addedIds = added.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        edges?.AddRange(evaluatedEdges.Where(edge => addedIds.Contains(edge.SourceId) || addedIds.Contains(edge.TargetId)));
        return new("1.0", projectContext, Guid.NewGuid().ToString(), scopes,
            exports.DistinctBy(export => export.Id).ToArray(), references.DistinctBy(reference => reference.Id).ToArray(), diagnostics);
    }

    private static string NodeLocation(IngestNodeRequest node) => node.Id + "|" + node.FilePath + "|" + node.LineNumber;
}
