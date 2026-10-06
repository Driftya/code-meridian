namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageDependency(
    string ProjectContext, PackageSymbolReference Reference, PackageBuildScope Scope,
    PackageReferenceResolution Resolution, CodeNode? Source, CodeNode? Target);
