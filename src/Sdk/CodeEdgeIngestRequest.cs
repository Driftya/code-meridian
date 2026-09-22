namespace CodeMeridian.Sdk;

public sealed record CodeEdgeIngestRequest(
    string SourceId,
    string TargetId,
    string Type,
    bool? IsAsync = null,
    string? CallSite = null,
    int? ParamCount = null,
    double? Confidence = null,
    Dictionary<string, string>? Properties = null,
    string? EvidenceKind = null,
    string? EvidenceReason = null,
    string? Resolver = null,
    string? SourceFilePath = null,
    int? SourceLine = null,
    int? SourceColumn = null,
    int? SourceEndLine = null,
    int? SourceEndColumn = null,
    Dictionary<string, string>? EvidenceDetails = null);
