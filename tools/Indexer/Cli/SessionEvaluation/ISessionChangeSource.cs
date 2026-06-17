namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal interface ISessionChangeSource
{
    Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken);
}
