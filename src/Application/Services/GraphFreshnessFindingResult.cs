namespace CodeMeridian.Application.Services;

public sealed record GraphFreshnessFindingResult(
    GraphNodeResult Node,
    string Confidence,
    string SourceVerification,
    string LineMetadata,
    DateTimeOffset? LastIndexedAt,
    DateTimeOffset? UpdatedAt,
    string Reason);
