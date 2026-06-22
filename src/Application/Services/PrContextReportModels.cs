namespace CodeMeridian.Application.Services;

public sealed record PrContextReportRequest(
    string? ProjectContext,
    IReadOnlyCollection<string> ChangedFiles,
    string? BaseRef = null,
    string? HeadRef = null,
    bool IncludeDocs = true,
    int ImpactDepth = 2,
    int Limit = 10);

public sealed record PrContextReport(
    string? ProjectContext,
    string? BaseRef,
    string? HeadRef,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<PrContextNodeSummary> ChangedNodes,
    IReadOnlyList<PrContextImpactSummary> ImpactedNodes,
    IReadOnlyList<PrContextNodeSummary> MissingTestNodes,
    IReadOnlyList<PrContextHotspotWarning> HotspotWarnings,
    IReadOnlyList<PrContextRelatedDocument> RelatedDocuments,
    IReadOnlyList<string> ReviewFocus);

public sealed record PrContextNodeSummary(
    string Id,
    string Name,
    string Type,
    string? FilePath,
    string? ProjectContext,
    int? LineNumber,
    int? LineCount);

public sealed record PrContextImpactSummary(
    PrContextNodeSummary Node,
    int Distance,
    int ChangedNodeMatches);

public sealed record PrContextHotspotWarning(
    PrContextNodeSummary Node,
    string Reason,
    int? FanIn = null,
    int? ChangeCount = null);

public sealed record PrContextRelatedDocument(
    string Id,
    string Source,
    string Confidence,
    double Score,
    IReadOnlyList<string> MatchedKeywords);
