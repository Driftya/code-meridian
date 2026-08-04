namespace CodeMeridian.Application.Services;

public sealed record ConnectionEdgeResult(
    int Order,
    string SourceId,
    string TargetId,
    string Relationship);
