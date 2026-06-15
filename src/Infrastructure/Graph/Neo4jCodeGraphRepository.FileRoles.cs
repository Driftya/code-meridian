namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private static string FileRoleExpression(string alias) =>
        $"coalesce({alias}.fileRole, 'Unknown')";
}
