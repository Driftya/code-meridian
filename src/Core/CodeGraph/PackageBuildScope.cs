namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageBuildScope(
    string Id, string ProjectPath, string TargetFramework, string Configuration,
    string AssemblyName, string AssemblyIdentity, string? PackageId, string? PackageVersion,
    string? RepositoryUrl, string? Commit, bool Dirty, string Provenance);
