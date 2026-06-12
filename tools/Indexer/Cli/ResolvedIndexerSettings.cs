using CodeMeridian.Tooling.Storage;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ResolvedIndexerSettings
{
    public required DirectoryInfo RootPath { get; init; }
    public required string Project { get; init; }
    public required string CodeMeridianUrl { get; init; }
    public string? ApiKey { get; init; }
    public bool Clear { get; init; }
    public bool RebuildKeywords { get; init; }
    public bool IncludeDocs { get; init; } = true;
    public bool Watch { get; init; }
    public bool DryRun { get; init; }
    public bool ListCapabilities { get; init; }
    public bool SkipCSharp { get; init; }
    public bool SkipTypeScript { get; init; }
    public bool SkipConfiguration { get; init; }
    public IReadOnlyList<string>? ConfigurationFiles { get; init; }
    public bool SkipDiagnostics { get; init; }
    public bool AllowRepoScripts { get; init; }
    public bool Incremental { get; init; } = true;
    public required IndexerStorageMode StorageMode { get; init; }
}
