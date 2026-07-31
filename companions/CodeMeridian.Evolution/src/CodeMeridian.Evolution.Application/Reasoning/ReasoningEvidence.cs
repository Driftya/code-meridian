namespace CodeMeridian.Evolution.Application.Reasoning;

public sealed record ReasoningEvidence(
    string Id,
    string Summary,
    string Provenance,
    decimal Confidence,
    string ProjectId);

