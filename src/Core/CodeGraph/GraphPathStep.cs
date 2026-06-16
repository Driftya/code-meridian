namespace CodeMeridian.Core.CodeGraph;

public sealed record GraphPathStep(
    CodeNode Node,
    string? RelationshipType,
    double? RelationshipConfidence);
