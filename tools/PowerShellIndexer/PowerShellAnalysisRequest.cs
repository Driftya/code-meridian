namespace CodeMeridian.PowerShellIndexer;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PowerShellAnalysisRequest(string Project, string RootPath, bool? CaseSensitive, IReadOnlyList<string> Files)
{
    public string ContractVersion { get; init; } = "1.0";
}
