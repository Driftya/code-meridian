namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageForwardingAssembly(
    string AssemblyIdentity, string PackageId, string PackageVersion, string CompileAsset);
