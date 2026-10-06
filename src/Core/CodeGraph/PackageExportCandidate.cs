namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageExportCandidate(
    PackageSymbolExport Export, PackageBuildScope Scope, string ProjectContext);
