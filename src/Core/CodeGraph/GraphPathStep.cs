namespace CodeMeridian.Core.CodeGraph;

public sealed record GraphPathStep(
    CodeNode Node,
    string? RelationshipType,
    double? RelationshipConfidence)
{
    public string? ViaRelationship => RelationshipType;
    public EdgeEvidenceKind EvidenceKind { get; init; } = EdgeEvidenceKind.Unknown;
    public string? EvidenceReason { get; init; }
    public string? Resolver { get; init; }
    public string? SourceFilePath { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public int? SourceEndLine { get; init; }
    public int? SourceEndColumn { get; init; }

    public static implicit operator GraphPathStep((CodeNode Node, string? ViaRelationship) step) =>
        new(step.Node, step.ViaRelationship, null);

    public void Deconstruct(out CodeNode node, out string? viaRelationship)
    {
        node = Node;
        viaRelationship = RelationshipType;
    }
}
