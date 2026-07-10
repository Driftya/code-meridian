namespace CodeMeridian.McpServer.GraphQl;

public sealed record GraphPropertyFilterInput
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}
