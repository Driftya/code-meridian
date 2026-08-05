namespace CodeMeridian.Application.Services;

public sealed record ImpactPathSegmentResult(
    int Order,
    GraphNodeResult Node,
    string? RelationshipType,
    double? RelationshipConfidence);
