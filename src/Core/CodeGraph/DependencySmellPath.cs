namespace CodeMeridian.Core.CodeGraph;

public sealed record DependencySmellPath(
    string Violation,
    CodeNode Source,
    CodeNode Target,
    int Distance,
    IReadOnlyList<GraphPathStep> Steps);
