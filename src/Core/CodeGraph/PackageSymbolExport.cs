namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageSymbolExport(
    string Id, string ScopeId, string SourceId, string SymbolKey, string FilePath,
    bool AmbiguousSource = false);
