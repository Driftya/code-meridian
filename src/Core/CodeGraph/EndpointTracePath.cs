namespace CodeMeridian.Core.CodeGraph;

public sealed record EndpointTracePath(
    IReadOnlyList<GraphPathStep> Steps);
