namespace CodeMeridian.Sdk;

public sealed record PrContextReportRequest(
    IReadOnlyCollection<string> ChangedFiles,
    string? ProjectContext = null,
    string? BaseRef = null,
    string? HeadRef = null,
    bool IncludeDocs = true,
    int ImpactDepth = 2,
    int Limit = 10);

public sealed record PrContextReportResponse(
    string? ProjectContext,
    string? BaseRef,
    string? HeadRef,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<PrContextNodeSummaryResponse> ChangedNodes,
    IReadOnlyList<PrContextImpactSummaryResponse> ImpactedNodes,
    IReadOnlyList<PrContextNodeSummaryResponse> MissingTestNodes,
    IReadOnlyList<PrContextHotspotWarningResponse> HotspotWarnings,
    IReadOnlyList<PrContextRelatedDocumentResponse> RelatedDocuments,
    IReadOnlyList<string> ReviewFocus);

public sealed record PrContextNodeSummaryResponse(
    string Id,
    string Name,
    string Type,
    string? FilePath,
    string? ProjectContext,
    int? LineNumber,
    int? LineCount);

public sealed record PrContextImpactSummaryResponse(
    PrContextNodeSummaryResponse Node,
    int Distance,
    int ChangedNodeMatches);

public sealed record PrContextHotspotWarningResponse(
    PrContextNodeSummaryResponse Node,
    string Reason,
    int? FanIn = null,
    int? ChangeCount = null);

public sealed record PrContextRelatedDocumentResponse(
    string Id,
    string Source,
    string Confidence,
    double Score,
    IReadOnlyList<string> MatchedKeywords);
