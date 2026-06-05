namespace CodeMeridian.Core.CodeGraph;

public sealed record CodeGraphQuery
{
    public string? ProjectContext { get; init; }
    public string? NameFilter { get; init; }
    public string? FilePathFilter { get; init; }
    public CodeNodeType? TypeFilter { get; init; }

    /// <summary>Full-text or semantic query matched against node name and summary.</summary>
    public string? SemanticQuery { get; init; }

    public int MaxDepth { get; init; } = 2;
    public int Limit { get; init; } = 20;
}
