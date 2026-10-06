namespace CodeMeridian.Core.CodeGraph;

public interface ISqlGraphRepository
{
    Task PublishSqlGraphAsync(SqlGraphSnapshot snapshot, CancellationToken cancellationToken = default);
}
