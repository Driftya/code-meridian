using CodeMeridian.Core.GraphQueries;

namespace CodeMeridian.McpServer.GraphQl;

public sealed record GraphSortInput
{
    public required string Field { get; init; }
    public GraphSortDirection Direction { get; init; } = GraphSortDirection.Ascending;
}
