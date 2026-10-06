namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageSymbolReference(
    string Id, string ScopeId, string SourceId, string SymbolKey,
    string AssemblyName, string AssemblyIdentity, string? PackageId, string? PackageVersion,
    string? RepositoryUrl, string? RepositoryCommit, string Relationship,
    string FilePath, int Line, int Column, int EndLine, int EndColumn,
    string? ProducerProject = null, string? CompileAsset = null, string? DependencyFramework = null,
    string DependencyKind = "nuget")
{
    // Referenced facades forwarding the bound type; these are origin hints, not source ownership.
    public IReadOnlyList<PackageForwardingAssembly> ForwardingAssemblies { get; init; } = [];
}
