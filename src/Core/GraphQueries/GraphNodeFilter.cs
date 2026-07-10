namespace CodeMeridian.Core.GraphQueries;

public sealed record GraphNodeFilter
{
    public IReadOnlyList<string> Labels { get; init; } = [];
    public string? ProjectContext { get; init; }
    public IReadOnlyDictionary<string, string> PropertyEquals { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> PropertyContains { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> NodeIds { get; init; } = [];
    public string? KeywordText { get; init; }
    public string? KeywordCategory { get; init; }
}
