namespace CodeMeridian.Application.Services;

public sealed record ImpactFindingResult(
    GraphNodeResult Node,
    int Distance,
    string Classification,
    string Reason,
    string Path,
    string EvidenceBucket,
    IReadOnlyList<ImpactPathSegmentResult> PathSegments);
