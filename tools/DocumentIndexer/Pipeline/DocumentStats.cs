namespace CodeMeridian.DocumentIndexer.Pipeline;

public sealed record DocumentStats(int DocumentsIndexed)
{
    public int FailedFiles { get; init; }
}
