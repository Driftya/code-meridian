namespace CodeMeridian.Application.Services;

public sealed record CrossProjectEdgeResult(GraphNodeResult Source, GraphNodeResult Target, string Relationship);
