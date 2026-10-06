namespace CodeMeridian.Sdk;

public sealed record PackageReferenceHealthResponse(IReadOnlyDictionary<string, long> Counts, IReadOnlyList<string> Diagnostics);
