namespace CodeMeridian.Core.CodeGraph;

public sealed record PendingPackageReference(
    PackageSymbolReference Reference, PackageBuildScope Scope, string ProjectContext, long Revision);
