namespace CodeMeridian.Tooling.Configuration;

public sealed record CodeMeridianConfigSnapshot(
    string? Project,
    string? CodeMeridianUrl,
    bool? AllowRepoScripts,
    bool? UseGlobalCache,
    IReadOnlyList<string>? ConfigurationFiles,
    string? ArchitecturePath,
    CodeMeridianFileRolePatternSnapshot? FileRoles,
    int Version);

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
    public string[]? ConfigurationFiles { get; set; }
    public CodeMeridianArchitectureOptions? Architecture { get; set; }
    public CodeMeridianIndexingOptions? Indexing { get; set; }
}

public sealed class CodeMeridianArchitectureOptions
{
    public string? Path { get; set; }
}

public sealed class CodeMeridianIndexingOptions
{
    public CodeMeridianFileRoleOptions? FileRoles { get; set; }
}

public sealed class CodeMeridianFileRoleOptions
{
    public string[]? Test { get; set; }
    public string[]? Migration { get; set; }
    public string[]? Snapshot { get; set; }
    public string[]? Generated { get; set; }
    public string[]? BuildArtifact { get; set; }
    public string[]? Configuration { get; set; }
}

public sealed record CodeMeridianFileRolePatternSnapshot(
    IReadOnlyList<string>? Test,
    IReadOnlyList<string>? Migration,
    IReadOnlyList<string>? Snapshot,
    IReadOnlyList<string>? Generated,
    IReadOnlyList<string>? BuildArtifact,
    IReadOnlyList<string>? Configuration);
