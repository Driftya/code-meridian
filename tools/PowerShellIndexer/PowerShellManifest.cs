namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellManifest(string Status, IReadOnlyDictionary<string, object?> Metadata,
    string? RootModule, IReadOnlyList<string>? FunctionsToExport);
