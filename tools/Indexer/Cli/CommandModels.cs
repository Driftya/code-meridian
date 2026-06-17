using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed record IndexCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl,
    bool Clear,
    bool RebuildKeywords,
    bool IncludeDocs,
    bool Watch,
    bool DryRun,
    bool ListCapabilities,
    bool SkipCSharp,
    bool SkipTypeScript,
    bool SkipConfiguration,
    bool SkipDiagnostics,
    bool AllowRepoScripts,
    bool Incremental,
    IndexerStorageMode? Storage);

internal sealed record ClearCommandOptions(
    string? Project,
    string? CodeMeridianUrl,
    bool ClearAllCodeGraph);

internal sealed record InitCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl,
    bool Force,
    bool Global);

internal sealed record DoctorCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl);

internal sealed record CheckDriftCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl,
    string FailOn);

internal sealed record KeywordCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl,
    bool Background = false,
    bool Wait = false,
    int? LeaseTtlSeconds = null,
    Guid? JobId = null,
    KeywordCommandAction Action = KeywordCommandAction.Rebuild);

internal sealed record ConfigurationCommandOptions(
    string? Path,
    string? Project,
    string? CodeMeridianUrl);

internal enum KeywordCommandAction
{
    Rebuild = 0,
    Classify = 1,
    Status = 2
}
