namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageReferenceHealth(IReadOnlyDictionary<string, long> Counts, IReadOnlyList<string> Diagnostics);
