namespace CodeMeridian.Core.CodeGraph;

public sealed record ImpactPath(
    CodeNode Node,
    int Distance,
    IReadOnlyList<GraphPathStep> Steps);
