namespace CodeMeridian.Indexer.Cli.Commands;

internal interface IPrContextGitDiffProvider
{
    Task<IReadOnlyCollection<string>> GetChangedFilesAsync(
        DirectoryInfo root,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken);
}
