namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellFileAnalysis(string Project, string Path, string FileId, string? SourceFingerprint, string Status,
    int ParseErrorCount, IReadOnlyList<PowerShellBody> Bodies, IReadOnlyList<PowerShellCommand> Commands,
    IReadOnlyList<PowerShellModuleReference> Modules, PowerShellManifest? Manifest, IReadOnlyList<string>? ExportedFunctions,
    IReadOnlyList<PowerShellDiagnostic> Diagnostics, int DiagnosticCount)
{
    public bool DefinitionsOnly { get; init; }
    public bool StaticModuleBody { get; init; }
    public bool HasDynamicCommandTable { get; init; }
    public string ContractVersion { get; init; } = "1.0";
}
