using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed record PackageDependencyResult(
    string ProjectContext, PackageSymbolReference Reference, PackageBuildScope Scope,
    PackageReferenceResolution Resolution, GraphNodeResult? Source, GraphNodeResult? Target)
{
    internal static PackageDependencyResult FromDependency(PackageDependency dependency) => new(
        dependency.ProjectContext, dependency.Reference, dependency.Scope, dependency.Resolution,
        dependency.Source is null ? null : GraphNodeResult.FromNode(dependency.Source),
        dependency.Target is null ? null : GraphNodeResult.FromNode(dependency.Target));
}
