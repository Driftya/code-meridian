namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageReferenceSnapshot(
    string ContractVersion, string ProjectContext, string Generation,
    IReadOnlyList<PackageBuildScope> Scopes,
    IReadOnlyList<PackageSymbolExport> Exports,
    IReadOnlyList<PackageSymbolReference> References,
    IReadOnlyList<string> Diagnostics);
