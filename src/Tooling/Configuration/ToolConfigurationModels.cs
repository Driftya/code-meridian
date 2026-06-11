namespace CodeMeridian.Tooling.Configuration;

public sealed record CodeMeridianConfigSnapshot(
    string? Project,
    string? CodeMeridianUrl,
    bool? AllowRepoScripts,
    bool? UseGlobalCache);

public sealed record ToolConfigurationContext(
    DirectoryInfo RootPath,
    CodeMeridianConfigSnapshot? LocalConfig,
    CodeMeridianConfigSnapshot? GlobalConfig,
    string? EnvironmentProject,
    string? EnvironmentUrl,
    string? ApiKey);

public sealed class CodeMeridianConfigFileOptions
{
    public string? Project { get; set; }
    public string? CodeMeridianUrl { get; set; }
    public string? Url { get; set; }
    public bool? AllowRepoScripts { get; set; }
    public bool? UseGlobalCache { get; set; }
}
