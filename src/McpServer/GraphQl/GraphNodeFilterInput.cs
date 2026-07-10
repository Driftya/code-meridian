namespace CodeMeridian.McpServer.GraphQl;

public sealed record GraphNodeFilterInput
{
    public IReadOnlyList<string>? Labels { get; init; }
    public string? ProjectContext { get; init; }
    public IReadOnlyList<GraphPropertyFilterInput>? PropertyEquals { get; init; }
    public IReadOnlyList<GraphPropertyFilterInput>? PropertyContains { get; init; }
    public IReadOnlyList<string>? NodeIds { get; init; }
    public string? KeywordText { get; init; }
    public string? KeywordCategory { get; init; }
}
